use std::{
    collections::BTreeMap,
    fs, io,
    path::{Path, PathBuf},
    sync::Mutex,
};

use serde::{Deserialize, Serialize};
use thiserror::Error;

use crate::platform::windows::fs::atomic_write;

const WINDOW_STATE_FILE_NAME: &str = "window-state.toml";

#[derive(Clone, Copy, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub(crate) struct PersistedWindowState {
    pub(crate) x: i32,
    pub(crate) y: i32,
    pub(crate) width: u32,
    pub(crate) height: u32,
    pub(crate) maximized: bool,
}

#[derive(Default, Deserialize, Serialize)]
struct WindowStateFile {
    windows: BTreeMap<String, PersistedWindowState>,
}

pub(crate) struct WindowStateStore {
    path: PathBuf,
    state: Mutex<WindowStateFile>,
}

impl WindowStateStore {
    pub(crate) fn load_or_create(config_dir: &Path) -> Result<Self, WindowStateError> {
        let path = config_dir.join(WINDOW_STATE_FILE_NAME);
        let state = match fs::read_to_string(&path) {
            Ok(content) => toml::from_str(&content).map_err(|source| WindowStateError::Parse {
                path: path.clone(),
                source,
            })?,
            Err(source) if source.kind() == io::ErrorKind::NotFound => WindowStateFile::default(),
            Err(source) => {
                return Err(WindowStateError::Read { path, source });
            }
        };

        Ok(Self {
            path,
            state: Mutex::new(state),
        })
    }

    pub(crate) fn get(&self, label: &str) -> Option<PersistedWindowState> {
        self.state
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .windows
            .get(label)
            .copied()
    }

    pub(crate) fn save(
        &self,
        label: &str,
        window: PersistedWindowState,
    ) -> Result<(), WindowStateError> {
        let mut state = self
            .state
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        state.windows.insert(label.to_owned(), window);
        let mut content = toml::to_string_pretty(&*state)?;
        content.push('\n');
        atomic_write(&self.path, content.as_bytes()).map_err(|source| WindowStateError::Write {
            path: self.path.clone(),
            source,
        })
    }
}

#[derive(Debug, Error)]
pub(crate) enum WindowStateError {
    #[error("failed to read window state from {path}: {source}")]
    Read { path: PathBuf, source: io::Error },
    #[error("failed to parse window state from {path}: {source}")]
    Parse {
        path: PathBuf,
        source: toml::de::Error,
    },
    #[error("failed to serialize window state: {0}")]
    Serialize(#[from] toml::ser::Error),
    #[error("failed to write window state to {path}: {source}")]
    Write { path: PathBuf, source: io::Error },
}

#[cfg(test)]
mod tests {
    use std::fs;

    use super::{PersistedWindowState, WindowStateStore};

    #[test]
    fn persists_state_by_window_label() {
        let directory =
            std::env::temp_dir().join(format!("juju-window-state-{}", std::process::id()));
        let _ = fs::remove_dir_all(&directory);
        fs::create_dir_all(&directory).expect("test directory should be created");
        let saved = PersistedWindowState {
            x: -400,
            y: 100,
            width: 1_400,
            height: 900,
            maximized: true,
        };

        let store = WindowStateStore::load_or_create(&directory).expect("store should load");
        store.save("tool-json", saved).expect("state should save");
        drop(store);

        let restored = WindowStateStore::load_or_create(&directory)
            .expect("store should reload")
            .get("tool-json");
        assert_eq!(restored, Some(saved));

        fs::remove_dir_all(directory).expect("test directory should be removed");
    }
}
