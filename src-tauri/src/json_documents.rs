use crate::settings::AppSettings;
use chrono::Utc;
use serde::{Deserialize, Serialize};
use std::collections::hash_map::DefaultHasher;
use std::fs;
use std::hash::{Hash, Hasher};
use std::path::{Path, PathBuf};
use tauri::AppHandle;
use uuid::Uuid;

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct JsonDocument {
    pub id: String,
    pub file_name: String,
    pub order: usize,
    pub created_at_utc: String,
    pub updated_at_utc: String,
    pub source: JsonDocumentSource,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct JsonDocumentSource {
    pub kind: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub original_file_name: Option<String>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct JsonDocumentSnapshot {
    pub document: JsonDocument,
    pub content: String,
    pub revision: String,
}

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct Metadata {
    schema_version: u8,
    sequence: Sequence,
    documents: Vec<JsonDocument>,
}

#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct Sequence {
    date: String,
    last: u64,
}

pub fn list(app: &AppHandle) -> Result<Vec<JsonDocument>, String> {
    let (directory, mut metadata) = read_metadata(app)?;
    let mut known: Vec<String> = metadata
        .documents
        .iter()
        .map(|item| item.file_name.to_lowercase())
        .collect();
    let mut added = false;
    for entry in fs::read_dir(&directory).map_err(|error| error.to_string())? {
        let entry = entry.map_err(|error| error.to_string())?;
        let path = entry.path();
        let Some(name) = path.file_name().and_then(|name| name.to_str()) else {
            continue;
        };
        if path
            .extension()
            .is_some_and(|extension| extension.eq_ignore_ascii_case("json"))
            && name != "metadata.json"
            && known
                .iter()
                .all(|existing| existing != &name.to_lowercase())
        {
            let now = Utc::now().to_rfc3339();
            metadata.documents.push(JsonDocument {
                id: Uuid::new_v4().to_string(),
                file_name: name.into(),
                order: metadata.documents.len(),
                created_at_utc: now.clone(),
                updated_at_utc: now,
                source: JsonDocumentSource {
                    kind: "recovered".into(),
                    original_file_name: None,
                },
            });
            known.push(name.to_lowercase());
            added = true;
        }
    }
    let document_count = metadata.documents.len();
    metadata
        .documents
        .retain(|item| directory.join(&item.file_name).is_file());
    let removed = metadata.documents.len() != document_count;
    normalize_orders(&mut metadata.documents);
    if added || removed {
        write_metadata(&directory, &metadata)?;
    }
    metadata.documents.sort_by_key(|item| item.order);
    Ok(metadata.documents)
}

pub fn open(app: &AppHandle, id: &str) -> Result<JsonDocumentSnapshot, String> {
    let (directory, metadata) = read_metadata(app)?;
    let document = find_document(&metadata, id)?.clone();
    let path = directory.join(&document.file_name);
    let content = fs::read_to_string(&path).map_err(|error| error.to_string())?;
    let revision = revision(&path, &content)?;
    Ok(JsonDocumentSnapshot {
        document,
        content,
        revision,
    })
}

pub fn create(app: &AppHandle) -> Result<JsonDocumentSnapshot, String> {
    let (directory, mut metadata) = read_metadata(app)?;
    let today = Utc::now().format("%Y%m%d").to_string();
    if metadata.sequence.date != today {
        metadata.sequence = Sequence {
            date: today.clone(),
            last: 0,
        };
    }
    let mut next = metadata.sequence.last + 1;
    let file_name = loop {
        let candidate = format!("{today}{next:03}.json");
        next += 1;
        if !directory.join(&candidate).exists() {
            break candidate;
        }
    };
    metadata.sequence.last = next - 1;
    let now = Utc::now().to_rfc3339();
    let document = JsonDocument {
        id: Uuid::new_v4().to_string(),
        file_name: file_name.clone(),
        order: 0,
        created_at_utc: now.clone(),
        updated_at_utc: now,
        source: JsonDocumentSource {
            kind: "created".into(),
            original_file_name: None,
        },
    };
    write_text_atomic(&directory.join(&file_name), "{}")?;
    metadata.documents.insert(0, document.clone());
    normalize_orders(&mut metadata.documents);
    if let Err(error) = write_metadata(&directory, &metadata) {
        let _ = fs::remove_file(directory.join(file_name));
        return Err(error);
    }
    open(app, &document.id)
}

pub fn save(app: &AppHandle, id: &str, content: &str, expected: &str) -> Result<String, String> {
    let (directory, mut metadata) = read_metadata(app)?;
    let index = metadata
        .documents
        .iter()
        .position(|item| item.id == id)
        .ok_or_else(|| "文档不存在。".to_string())?;
    let path = directory.join(&metadata.documents[index].file_name);
    let current = fs::read_to_string(&path).map_err(|error| error.to_string())?;
    if revision(&path, &current)? != expected {
        return Err("文件已在外部修改；请重新加载或确认覆盖。".into());
    }
    write_text_atomic(&path, content)?;
    metadata.documents[index].updated_at_utc = Utc::now().to_rfc3339();
    write_metadata(&directory, &metadata)?;
    revision(&path, content)
}

pub fn overwrite(app: &AppHandle, id: &str, content: &str) -> Result<String, String> {
    let (directory, mut metadata) = read_metadata(app)?;
    let document = metadata
        .documents
        .iter_mut()
        .find(|item| item.id == id)
        .ok_or_else(|| "文档不存在。".to_string())?;
    let path = directory.join(&document.file_name);
    write_text_atomic(&path, content)?;
    document.updated_at_utc = Utc::now().to_rfc3339();
    write_metadata(&directory, &metadata)?;
    revision(&path, content)
}

pub fn rename(app: &AppHandle, id: &str, name: &str) -> Result<JsonDocument, String> {
    let (directory, mut metadata) = read_metadata(app)?;
    let index = metadata
        .documents
        .iter()
        .position(|item| item.id == id)
        .ok_or_else(|| "文档不存在。".to_string())?;
    let file_name = normalize_name(name)?;
    let target = directory.join(&file_name);
    if target.exists() {
        return Err("同名文档已存在。".into());
    }
    let previous = metadata.documents[index].file_name.clone();
    fs::rename(directory.join(&previous), &target).map_err(|error| error.to_string())?;
    metadata.documents[index].file_name = file_name;
    metadata.documents[index].updated_at_utc = Utc::now().to_rfc3339();
    if let Err(error) = write_metadata(&directory, &metadata) {
        let _ = fs::rename(target, directory.join(previous));
        return Err(error);
    }
    Ok(metadata.documents[index].clone())
}

pub fn delete(app: &AppHandle, id: &str) -> Result<(), String> {
    let (directory, mut metadata) = read_metadata(app)?;
    let index = metadata
        .documents
        .iter()
        .position(|item| item.id == id)
        .ok_or_else(|| "文档不存在。".to_string())?;
    let document = metadata.documents.remove(index);
    let trash = directory
        .parent()
        .ok_or_else(|| "无法定位 JSON 工具目录。".to_string())?
        .join(".trash");
    fs::create_dir_all(&trash).map_err(|error| error.to_string())?;
    let stamp = Utc::now().format("%Y%m%d-%H%M%S");
    let trash_path = trash.join(format!("{stamp}__{}", document.file_name));
    fs::rename(directory.join(&document.file_name), &trash_path)
        .map_err(|error| error.to_string())?;
    normalize_orders(&mut metadata.documents);
    if let Err(error) = write_metadata(&directory, &metadata) {
        let _ = fs::rename(trash_path, directory.join(document.file_name));
        return Err(error);
    }
    Ok(())
}

pub fn reorder(app: &AppHandle, id: &str, order: usize) -> Result<Vec<JsonDocument>, String> {
    let (directory, mut metadata) = read_metadata(app)?;
    let current = metadata
        .documents
        .iter()
        .position(|item| item.id == id)
        .ok_or_else(|| "文档不存在。".to_string())?;
    let document = metadata.documents.remove(current);
    metadata
        .documents
        .insert(order.min(metadata.documents.len()), document);
    normalize_orders(&mut metadata.documents);
    write_metadata(&directory, &metadata)?;
    Ok(metadata.documents)
}

pub fn import(app: &AppHandle, paths: Vec<String>) -> Result<Vec<JsonDocument>, String> {
    let (directory, mut metadata) = read_metadata(app)?;
    for source in paths {
        let source_path = PathBuf::from(source);
        if !source_path
            .extension()
            .is_some_and(|extension| extension.eq_ignore_ascii_case("json"))
        {
            continue;
        }
        let original = source_path
            .file_name()
            .and_then(|name| name.to_str())
            .ok_or_else(|| "导入文件名无效。".to_string())?;
        let stem = Path::new(original)
            .file_stem()
            .and_then(|name| name.to_str())
            .unwrap_or("document");
        let mut name = normalize_name(original)?;
        let mut suffix = 2;
        while directory.join(&name).exists() {
            name = normalize_name(&format!("{stem}-{suffix}.json"))?;
            suffix += 1;
        }
        let content = fs::read_to_string(&source_path).map_err(|error| error.to_string())?;
        write_text_atomic(&directory.join(&name), &content)?;
        let now = Utc::now().to_rfc3339();
        metadata.documents.push(JsonDocument {
            id: Uuid::new_v4().to_string(),
            file_name: name,
            order: metadata.documents.len(),
            created_at_utc: now.clone(),
            updated_at_utc: now,
            source: JsonDocumentSource {
                kind: "imported".into(),
                original_file_name: Some(original.into()),
            },
        });
    }
    normalize_orders(&mut metadata.documents);
    write_metadata(&directory, &metadata)?;
    Ok(metadata.documents)
}

pub fn copy_file_to_clipboard(app: &AppHandle, id: &str) -> Result<String, String> {
    let snapshot = open(app, id)?;
    let root = PathBuf::from(AppSettings::load(app)?.data_root);
    let cache = root
        .join("tools")
        .join("json")
        .join("cache")
        .join("clipboard");
    fs::create_dir_all(&cache).map_err(|error| error.to_string())?;
    clean_expired_clipboard_files(&cache)?;
    let timestamp = Utc::now().format("%Y%m%d-%H%M%S");
    let snapshot_path = cache.join(format!("{timestamp}__{}", snapshot.document.file_name));
    write_text_atomic(&snapshot_path, &snapshot.content)?;
    set_file_drop_clipboard(&snapshot_path)?;
    Ok(snapshot_path.to_string_lossy().into_owned())
}

fn clean_expired_clipboard_files(directory: &Path) -> Result<(), String> {
    let now = std::time::SystemTime::now();
    for entry in fs::read_dir(directory).map_err(|error| error.to_string())? {
        let entry = entry.map_err(|error| error.to_string())?;
        let metadata = entry.metadata().map_err(|error| error.to_string())?;
        if metadata
            .modified()
            .ok()
            .and_then(|modified| now.duration_since(modified).ok())
            .is_some_and(|age| age > std::time::Duration::from_secs(3 * 24 * 60 * 60))
        {
            let _ = fs::remove_file(entry.path());
        }
    }
    Ok(())
}

#[cfg(windows)]
fn set_file_drop_clipboard(path: &Path) -> Result<(), String> {
    use std::os::windows::ffi::OsStrExt;
    use windows_sys::Win32::Foundation::GlobalFree;
    use windows_sys::Win32::System::DataExchange::{
        CloseClipboard, EmptyClipboard, OpenClipboard, SetClipboardData,
    };
    use windows_sys::Win32::System::Memory::{
        GMEM_MOVEABLE, GlobalAlloc, GlobalLock, GlobalUnlock,
    };

    #[repr(C)]
    struct DropFiles {
        offset: u32,
        x: i32,
        y: i32,
        non_client: i32,
        wide: i32,
    }

    let wide_path: Vec<u16> = path
        .as_os_str()
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();
    let header = DropFiles {
        offset: std::mem::size_of::<DropFiles>() as u32,
        x: 0,
        y: 0,
        non_client: 0,
        wide: 1,
    };
    let header_bytes = unsafe {
        std::slice::from_raw_parts(
            (&header as *const DropFiles).cast::<u8>(),
            std::mem::size_of::<DropFiles>(),
        )
    };
    let mut bytes = Vec::with_capacity(header_bytes.len() + wide_path.len() * 2);
    bytes.extend_from_slice(header_bytes);
    for character in wide_path {
        bytes.extend_from_slice(&character.to_le_bytes());
    }
    bytes.extend_from_slice(&[0, 0]);

    unsafe {
        if OpenClipboard(std::ptr::null_mut()) == 0 {
            return Err("无法打开 Windows 剪贴板。".into());
        }
        if EmptyClipboard() == 0 {
            CloseClipboard();
            return Err("无法清空 Windows 剪贴板。".into());
        }
        let memory = GlobalAlloc(GMEM_MOVEABLE, bytes.len());
        if memory.is_null() {
            CloseClipboard();
            return Err("无法分配剪贴板文件数据。".into());
        }
        let destination = GlobalLock(memory).cast::<u8>();
        if destination.is_null() {
            GlobalFree(memory);
            CloseClipboard();
            return Err("无法锁定剪贴板文件数据。".into());
        }
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), destination, bytes.len());
        GlobalUnlock(memory);
        if SetClipboardData(15, memory).is_null() {
            GlobalFree(memory);
            CloseClipboard();
            return Err("无法将 JSON 文件复制到剪贴板。".into());
        }
        CloseClipboard();
    }
    Ok(())
}

#[cfg(not(windows))]
fn set_file_drop_clipboard(_path: &Path) -> Result<(), String> {
    Err("文件剪贴板仅支持 Windows。".into())
}

fn read_metadata(app: &AppHandle) -> Result<(PathBuf, Metadata), String> {
    let root = PathBuf::from(AppSettings::load(app)?.data_root);
    let directory = root.join("tools").join("json").join("documents");
    fs::create_dir_all(&directory).map_err(|error| error.to_string())?;
    let metadata_path = directory
        .parent()
        .ok_or_else(|| "无法定位 JSON 工具目录。".to_string())?
        .join("metadata.json");
    let mut corrupted = false;
    let mut metadata = if metadata_path.exists() {
        let bytes = fs::read(&metadata_path).map_err(|error| error.to_string())?;
        match serde_json::from_slice::<Metadata>(&bytes) {
            Ok(metadata) => metadata,
            Err(_) => {
                let backup = metadata_path.with_extension(format!(
                    "json.corrupt-{}",
                    Utc::now().format("%Y%m%d%H%M%S%3f")
                ));
                let _ = fs::copy(&metadata_path, backup);
                corrupted = true;
                Metadata::default()
            }
        }
    } else {
        Metadata {
            schema_version: 1,
            sequence: Sequence {
                date: Utc::now().format("%Y%m%d").to_string(),
                last: 0,
            },
            documents: Vec::new(),
        }
    };
    if metadata.schema_version != 1 {
        corrupted = true;
        metadata = Metadata {
            schema_version: 1,
            sequence: Sequence {
                date: Utc::now().format("%Y%m%d").to_string(),
                last: 0,
            },
            documents: Vec::new(),
        };
    }
    if !metadata_path.exists() || corrupted {
        write_metadata(&directory, &metadata)?;
    }
    Ok((directory, metadata))
}

fn find_document<'a>(metadata: &'a Metadata, id: &str) -> Result<&'a JsonDocument, String> {
    metadata
        .documents
        .iter()
        .find(|item| item.id == id)
        .ok_or_else(|| "文档不存在。".into())
}

fn normalize_orders(documents: &mut [JsonDocument]) {
    documents.sort_by_key(|item| item.order);
    for (order, document) in documents.iter_mut().enumerate() {
        document.order = order;
    }
}

fn normalize_name(name: &str) -> Result<String, String> {
    let name = name.trim();
    if name.is_empty()
        || name.contains("..")
        || name.chars().any(|character| {
            matches!(
                character,
                '<' | '>' | ':' | '"' | '/' | '\\' | '|' | '?' | '*'
            ) || character.is_control()
        })
    {
        return Err("文件名包含无效字符。".into());
    }
    let stem = Path::new(name)
        .file_stem()
        .and_then(|stem| stem.to_str())
        .unwrap_or("");
    let upper = stem.to_uppercase();
    if [
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8",
        "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ]
    .contains(&upper.as_str())
    {
        return Err("文件名不能使用 Windows 保留设备名。".into());
    }
    let file_name = if name.to_lowercase().ends_with(".json") {
        name.to_string()
    } else {
        format!("{name}.json")
    };
    Ok(file_name)
}

fn revision(path: &Path, content: &str) -> Result<String, String> {
    let modified = fs::metadata(path)
        .and_then(|metadata| metadata.modified())
        .map_err(|error| error.to_string())?
        .duration_since(std::time::UNIX_EPOCH)
        .map_err(|error| error.to_string())?
        .as_nanos();
    let mut hasher = DefaultHasher::new();
    content.hash(&mut hasher);
    Ok(format!(
        "{}:{modified}:{:016x}",
        content.len(),
        hasher.finish()
    ))
}

fn write_metadata(directory: &Path, metadata: &Metadata) -> Result<(), String> {
    let path = directory
        .parent()
        .ok_or_else(|| "无法定位 JSON 工具目录。".to_string())?
        .join("metadata.json");
    let contents = serde_json::to_string_pretty(metadata).map_err(|error| error.to_string())?;
    write_text_atomic(&path, &contents)
}

fn write_text_atomic(path: &Path, content: &str) -> Result<(), String> {
    let temporary = path.with_extension("json.tmp");
    fs::write(&temporary, content).map_err(|error| error.to_string())?;
    fs::rename(&temporary, path).map_err(|error| error.to_string())
}

#[cfg(test)]
mod tests {
    use super::{normalize_name, revision, write_text_atomic};
    use std::fs;

    #[test]
    fn json_document_names_are_normalized_and_reject_windows_paths() {
        assert_eq!(
            normalize_name(" report ").expect("name is valid"),
            "report.json"
        );
        assert_eq!(
            normalize_name("report.JSON").expect("extension is preserved"),
            "report.JSON"
        );
        assert!(normalize_name("../outside.json").is_err());
        assert!(normalize_name("CON.json").is_err());
    }

    #[test]
    fn document_revision_changes_when_file_content_changes() {
        let temporary = tempfile::tempdir().expect("temporary directory");
        let file = temporary.path().join("document.json");
        write_text_atomic(&file, "{} ").expect("write initial JSON");
        let first = revision(&file, "{} ").expect("create revision");
        write_text_atomic(&file, "{\"a\":1}").expect("write changed JSON");
        let second = revision(&file, "{\"a\":1}").expect("create updated revision");
        assert_ne!(first, second);
        assert_eq!(
            fs::read_to_string(file).expect("read changed document"),
            "{\"a\":1}"
        );
    }
}
