use std::{
    collections::{hash_map::DefaultHasher, HashMap},
    fs,
    io,
    path::{Path, PathBuf},
    sync::{Arc, Mutex, RwLock},
    hash::{Hash, Hasher},
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

use notify::{recommended_watcher, EventKind, RecommendedWatcher, RecursiveMode, Watcher};
use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Emitter};
use thiserror::Error;

use crate::platform::windows::fs::atomic_write;

const DATA_MANIFEST: &str = "juju.toml";
const DATA_SCHEMA_VERSION: u32 = 1;
const JSON_DIRECTORY: &str = "json";
const DOCUMENTS_DIRECTORY: &str = "documents";
const TRASH_DIRECTORY: &str = ".trash";
const SELF_WRITE_WINDOW: Duration = Duration::from_secs(2);
const WATCH_DEBOUNCE: Duration = Duration::from_millis(150);

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct JsonDocument {
    pub(crate) document_id: String,
    pub(crate) revision: String,
    pub(crate) content: String,
}

#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct JsonDocumentSummary {
    pub(crate) document_id: String,
    pub(crate) revision: String,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ExternalChange {
    document_id: String,
    revision: Option<String>,
    kind: &'static str,
}

#[derive(Debug, Error)]
pub(crate) enum StorageError {
    #[error("data root is unavailable: {0}")]
    DataRootUnavailable(PathBuf),
    #[error("data root is not a juju data root: {0}")]
    InvalidDataRoot(PathBuf),
    #[error("invalid document name: {0}")]
    InvalidDocumentName(String),
    #[error("document was not found: {0}")]
    DocumentNotFound(String),
    #[error("document already exists: {0}")]
    DocumentAlreadyExists(String),
    #[error("document was modified outside juju")]
    ExternalModificationConflict,
    #[error("storage I/O failed for {path}: {source}")]
    Io { path: PathBuf, source: io::Error },
    #[error("failed to start document watcher: {0}")]
    Watcher(#[from] notify::Error),
}

pub(crate) struct StorageManager {
    root: RwLock<PathBuf>,
    self_writes: Arc<Mutex<HashMap<String, Instant>>>,
    recent_events: Arc<Mutex<HashMap<String, Instant>>>,
    watcher: Mutex<Option<RecommendedWatcher>>,
}

impl StorageManager {
    pub(crate) fn load_or_create(root: PathBuf) -> Result<Self, StorageError> {
        initialize_data_root(&root)?;
        Ok(Self {
            root: RwLock::new(root),
            self_writes: Arc::new(Mutex::new(HashMap::new())),
            recent_events: Arc::new(Mutex::new(HashMap::new())),
            watcher: Mutex::new(None),
        })
    }

    pub(crate) fn root(&self) -> PathBuf {
        self.root
            .read()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .clone()
    }

    pub(crate) fn use_existing_root(&self, root: PathBuf) -> Result<(), StorageError> {
        if root.is_dir()
            && fs::read_dir(&root)
                .map_err(|source| StorageError::Io { path: root.clone(), source })?
                .next()
                .is_none()
        {
            initialize_data_root(&root)?;
        } else {
            validate_existing_data_root(&root)?;
        }
        *self.root.write().unwrap_or_else(|poisoned| poisoned.into_inner()) = root;
        Ok(())
    }

    pub(crate) fn migrate_to(&self, destination: PathBuf) -> Result<(), StorageError> {
        let source = self.root();
        if source == destination {
            return Ok(());
        }
        if destination.starts_with(&source) {
            return Err(StorageError::InvalidDataRoot(destination));
        }
        if destination.exists()
            && fs::read_dir(&destination)
                .map_err(|source| StorageError::Io { path: destination.clone(), source })?
                .next()
                .is_some()
        {
            return Err(StorageError::DocumentAlreadyExists(destination.display().to_string()));
        }
        copy_directory(&source, &destination)?;
        validate_existing_data_root(&destination)?;
        *self.root.write().unwrap_or_else(|poisoned| poisoned.into_inner()) = destination;
        Ok(())
    }

    pub(crate) fn list_documents(&self) -> Result<Vec<JsonDocumentSummary>, StorageError> {
        let directory = self.documents_dir();
        let mut documents = Vec::new();
        for entry in fs::read_dir(&directory).map_err(|source| StorageError::Io {
            path: directory.clone(),
            source,
        })? {
            let entry = entry.map_err(|source| StorageError::Io {
                path: directory.clone(),
                source,
            })?;
            if !entry.file_type().map_err(|source| StorageError::Io {
                path: entry.path(),
                source,
            })?.is_file() {
                continue;
            }
            let document_id = entry.file_name().to_string_lossy().into_owned();
            if JsonDocumentId::parse(&document_id).is_ok() {
                documents.push(JsonDocumentSummary {
                    revision: revision_for(&entry.path())?,
                    document_id,
                });
            }
        }
        documents.sort_by(|left, right| left.document_id.cmp(&right.document_id));
        Ok(documents)
    }

    pub(crate) fn create_document(&self) -> Result<JsonDocument, StorageError> {
        let directory = self.documents_dir();
        for index in 1.. {
            let id = format!("未命名-{index}.json");
            let path = directory.join(&id);
            if !path.exists() {
                atomic_write(&path, b"{}\n").map_err(|source| StorageError::Io {
                    path: path.clone(),
                    source,
                })?;
                self.remember_self_write(&id);
                return self.read_document(id);
            }
        }
        unreachable!("unbounded document index exhausted")
    }

    pub(crate) fn read_document(&self, document_id: String) -> Result<JsonDocument, StorageError> {
        let id = JsonDocumentId::parse(&document_id)?;
        let path = self.documents_dir().join(&id.0);
        let content = fs::read_to_string(&path).map_err(|source| map_read_error(path.clone(), source, &id.0))?;
        Ok(JsonDocument {
            document_id: id.0,
            revision: revision_for(&path)?,
            content,
        })
    }

    pub(crate) fn write_document(
        &self,
        document_id: String,
        content: String,
        expected_revision: String,
    ) -> Result<JsonDocument, StorageError> {
        let id = JsonDocumentId::parse(&document_id)?;
        let path = self.documents_dir().join(&id.0);
        let actual_revision = revision_for(&path).map_err(|source| match source {
            StorageError::Io { source, .. } if source.kind() == io::ErrorKind::NotFound => {
                StorageError::DocumentNotFound(id.0.clone())
            }
            other => other,
        })?;
        if actual_revision != expected_revision {
            return Err(StorageError::ExternalModificationConflict);
        }
        atomic_write(&path, content.as_bytes()).map_err(|source| StorageError::Io {
            path: path.clone(),
            source,
        })?;
        self.remember_self_write(&id.0);
        Ok(JsonDocument {
            document_id: id.0,
            revision: revision_for(&path)?,
            content,
        })
    }

    pub(crate) fn rename_document(&self, document_id: String, new_name: String) -> Result<(), StorageError> {
        let old_id = JsonDocumentId::parse(&document_id)?;
        let new_id = JsonDocumentId::parse(&new_name)?;
        let directory = self.documents_dir();
        let old_path = directory.join(&old_id.0);
        let new_path = directory.join(&new_id.0);
        if new_path.exists() {
            return Err(StorageError::DocumentAlreadyExists(new_id.0));
        }
        fs::rename(&old_path, &new_path).map_err(|source| map_read_error(old_path, source, &old_id.0))?;
        Ok(())
    }

    pub(crate) fn delete_document(&self, document_id: String) -> Result<(), StorageError> {
        let id = JsonDocumentId::parse(&document_id)?;
        let source = self.documents_dir().join(&id.0);
        let trash = self.root().join(JSON_DIRECTORY).join(TRASH_DIRECTORY);
        let stamp = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_secs();
        let destination = trash.join(format!("{stamp}__{}", id.0));
        fs::rename(&source, &destination).map_err(|source_error| map_read_error(source, source_error, &id.0))?;
        Ok(())
    }

    pub(crate) fn start_watching(&self, app: AppHandle) -> Result<(), StorageError> {
        let directory = self.documents_dir();
        let self_writes = Arc::clone(&self.self_writes);
        let recent_events = Arc::clone(&self.recent_events);
        let mut watcher = recommended_watcher(move |event: notify::Result<notify::Event>| {
            let Ok(event) = event else { return };
            if !matches!(event.kind, EventKind::Modify(_) | EventKind::Create(_) | EventKind::Remove(_)) {
                return;
            }
            for path in event.paths {
                let Some(file_name) = path.file_name().and_then(|name| name.to_str()) else { continue };
                if JsonDocumentId::parse(file_name).is_err() {
                    continue;
                }
                let mut writes = self_writes.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
                writes.retain(|_, recorded| recorded.elapsed() < SELF_WRITE_WINDOW);
                if writes.contains_key(file_name) {
                    continue;
                }
                drop(writes);
                let mut emitted = recent_events.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
                emitted.retain(|_, recorded| recorded.elapsed() < WATCH_DEBOUNCE);
                if emitted.contains_key(file_name) {
                    continue;
                }
                emitted.insert(file_name.to_owned(), Instant::now());
                drop(emitted);
                let revision = revision_for(&path).ok();
                let kind = if path.exists() { "modified" } else { "deleted" };
                let _ = app.emit("json://external-change", ExternalChange {
                    document_id: file_name.to_owned(), revision, kind,
                });
            }
        })?;
        watcher.watch(&directory, RecursiveMode::NonRecursive)?;
        *self.watcher.lock().unwrap_or_else(|poisoned| poisoned.into_inner()) = Some(watcher);
        Ok(())
    }

    fn documents_dir(&self) -> PathBuf {
        self.root().join(JSON_DIRECTORY).join(DOCUMENTS_DIRECTORY)
    }

    fn remember_self_write(&self, document_id: &str) {
        self.self_writes
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .insert(document_id.to_owned(), Instant::now());
    }
}

struct JsonDocumentId(String);

impl JsonDocumentId {
    fn parse(value: &str) -> Result<Self, StorageError> {
        let invalid = value.is_empty()
            || !value.ends_with(".json")
            || value.contains(['/', '\\'])
            || value.contains("..")
            || value.trim() != value
            || value.chars().any(|character| matches!(character, '<' | '>' | ':' | '"' | '|' | '?' | '*'));
        let stem = value.strip_suffix(".json").unwrap_or_default();
        let reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
        if invalid || stem.is_empty() || reserved.iter().any(|name| stem.eq_ignore_ascii_case(name)) {
            return Err(StorageError::InvalidDocumentName(value.to_owned()));
        }
        Ok(Self(value.to_owned()))
    }
}

fn initialize_data_root(root: &Path) -> Result<(), StorageError> {
    fs::create_dir_all(root.join(JSON_DIRECTORY).join(DOCUMENTS_DIRECTORY)).map_err(|source| StorageError::Io { path: root.to_path_buf(), source })?;
    fs::create_dir_all(root.join(JSON_DIRECTORY).join(TRASH_DIRECTORY)).map_err(|source| StorageError::Io { path: root.to_path_buf(), source })?;
    let manifest = root.join(DATA_MANIFEST);
    if !manifest.exists() {
        atomic_write(&manifest, format!("data_schema_version = {DATA_SCHEMA_VERSION}\n").as_bytes())
            .map_err(|source| StorageError::Io { path: manifest, source })?;
    }
    validate_existing_data_root(root)
}

fn validate_existing_data_root(root: &Path) -> Result<(), StorageError> {
    if !root.is_dir() {
        return Err(StorageError::DataRootUnavailable(root.to_path_buf()));
    }
    let manifest = root.join(DATA_MANIFEST);
    let content = fs::read_to_string(&manifest).map_err(|source| StorageError::Io { path: manifest.clone(), source })?;
    let manifest: DataManifest = toml::from_str(&content).map_err(|_| StorageError::InvalidDataRoot(root.to_path_buf()))?;
    if manifest.data_schema_version != DATA_SCHEMA_VERSION {
        return Err(StorageError::InvalidDataRoot(root.to_path_buf()));
    }
    if !root.join(JSON_DIRECTORY).join(DOCUMENTS_DIRECTORY).is_dir() {
        return Err(StorageError::InvalidDataRoot(root.to_path_buf()));
    }
    Ok(())
}

#[derive(Deserialize)]
struct DataManifest {
    data_schema_version: u32,
}

fn revision_for(path: &Path) -> Result<String, StorageError> {
    let metadata = fs::metadata(path).map_err(|source| StorageError::Io { path: path.to_path_buf(), source })?;
    let modified = metadata.modified().unwrap_or(UNIX_EPOCH).duration_since(UNIX_EPOCH).unwrap_or_default().as_nanos();
    let content = fs::read(path).map_err(|source| StorageError::Io { path: path.to_path_buf(), source })?;
    let mut hasher = DefaultHasher::new();
    content.hash(&mut hasher);
    Ok(format!("{modified}:{}:{:x}", metadata.len(), hasher.finish()))
}

fn map_read_error(path: PathBuf, source: io::Error, document_id: &str) -> StorageError {
    if source.kind() == io::ErrorKind::NotFound {
        StorageError::DocumentNotFound(document_id.to_owned())
    } else {
        StorageError::Io { path, source }
    }
}

fn copy_directory(source: &Path, destination: &Path) -> Result<(), StorageError> {
    fs::create_dir_all(destination).map_err(|error| StorageError::Io { path: destination.to_path_buf(), source: error })?;
    for entry in fs::read_dir(source).map_err(|error| StorageError::Io { path: source.to_path_buf(), source: error })? {
        let entry = entry.map_err(|error| StorageError::Io { path: source.to_path_buf(), source: error })?;
        let target = destination.join(entry.file_name());
        if entry.file_type().map_err(|error| StorageError::Io { path: entry.path(), source: error })?.is_dir() {
            copy_directory(&entry.path(), &target)?;
        } else {
            fs::copy(entry.path(), &target).map_err(|error| StorageError::Io { path: target, source: error })?;
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use std::fs;
    use super::{StorageError, StorageManager};

    fn temporary_root(name: &str) -> std::path::PathBuf {
        let path = std::env::temp_dir().join(format!("juju-storage-{name}-{}", std::process::id()));
        let _ = fs::remove_dir_all(&path);
        path
    }

    #[test]
    fn creates_manifest_and_cruds_documents_with_revisions() {
        let root = temporary_root("crud");
        let storage = StorageManager::load_or_create(root.clone()).unwrap();
        assert!(root.join("juju.toml").is_file());
        let created = storage.create_document().unwrap();
        assert_eq!(created.document_id, "未命名-1.json");
        let saved = storage.write_document(created.document_id.clone(), "{\"ok\":true}".into(), created.revision).unwrap();
        assert_eq!(storage.read_document(saved.document_id.clone()).unwrap().content, "{\"ok\":true}");
        storage.rename_document(saved.document_id.clone(), "renamed.json".into()).unwrap();
        storage.delete_document("renamed.json".into()).unwrap();
        assert!(storage.list_documents().unwrap().is_empty());
        assert!(root.join("json").join(".trash").read_dir().unwrap().next().is_some());
        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn rejects_stale_and_unsafe_document_requests() {
        let root = temporary_root("conflict");
        let storage = StorageManager::load_or_create(root.clone()).unwrap();
        let created = storage.create_document().unwrap();
        fs::write(root.join("json").join("documents").join(&created.document_id), "{\"external\": true}").unwrap();
        let error = storage.write_document(created.document_id.clone(), "{}".into(), created.revision).unwrap_err();
        assert!(matches!(error, StorageError::ExternalModificationConflict));
        assert!(matches!(storage.read_document("../escape.json".into()), Err(StorageError::InvalidDocumentName(_))));
        fs::remove_dir_all(root).unwrap();
    }
}
