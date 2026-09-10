use std::{
    fs, io,
    path::{Path, PathBuf},
    sync::RwLock,
};

use serde::{Deserialize, Serialize};
use thiserror::Error;

use crate::platform::windows::fs::atomic_write;

const CURRENT_SCHEMA_VERSION: u32 = 1;
const CONFIG_FILE_NAME: &str = "config.toml";

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub(crate) struct AppSettings {
    pub(crate) schema_version: u32,
    pub(crate) data_root: PathBuf,
    pub(crate) app: GeneralSettings,
    pub(crate) launcher: LauncherSettings,
    pub(crate) storage: StorageSettings,
}

impl AppSettings {
    fn defaults(data_root: PathBuf) -> Self {
        Self {
            schema_version: CURRENT_SCHEMA_VERSION,
            data_root,
            app: GeneralSettings::default(),
            launcher: LauncherSettings::default(),
            storage: StorageSettings::default(),
        }
    }

    fn validate(&self) -> Result<(), SettingsError> {
        if self.schema_version != CURRENT_SCHEMA_VERSION {
            return Err(SettingsError::UnsupportedSchema(self.schema_version));
        }
        if !self.data_root.is_absolute() {
            return Err(SettingsError::InvalidConfig(
                "data_root must be an absolute path".into(),
            ));
        }
        if self.launcher.shortcut.trim().is_empty() {
            return Err(SettingsError::InvalidConfig(
                "launcher.shortcut cannot be empty".into(),
            ));
        }
        if self.launcher.timeout_ms == 0 {
            return Err(SettingsError::InvalidConfig(
                "launcher.timeout_ms must be greater than zero".into(),
            ));
        }
        if self.storage.autosave_debounce_ms == 0 {
            return Err(SettingsError::InvalidConfig(
                "storage.autosave_debounce_ms must be greater than zero".into(),
            ));
        }

        Ok(())
    }
}

#[derive(Clone, Debug, Default, Deserialize, Eq, PartialEq, Serialize)]
pub(crate) struct GeneralSettings {
    pub(crate) autostart: bool,
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub(crate) struct LauncherSettings {
    pub(crate) timeout_ms: u64,
    pub(crate) shortcut: String,
}

impl Default for LauncherSettings {
    fn default() -> Self {
        Self {
            timeout_ms: 2_000,
            shortcut: "Ctrl+Shift+Alt+Space".into(),
        }
    }
}

#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
pub(crate) struct StorageSettings {
    pub(crate) watch_external_changes: bool,
    pub(crate) autosave_debounce_ms: u64,
}

impl Default for StorageSettings {
    fn default() -> Self {
        Self {
            watch_external_changes: true,
            autosave_debounce_ms: 800,
        }
    }
}

pub(crate) struct SettingsService {
    config_path: PathBuf,
    settings: RwLock<AppSettings>,
}

impl SettingsService {
    pub(crate) fn load_or_create(
        config_dir: PathBuf,
        default_data_root: PathBuf,
    ) -> Result<Self, SettingsError> {
        create_app_directories(&config_dir)?;
        let config_path = config_dir.join(CONFIG_FILE_NAME);
        let settings = match fs::read_to_string(&config_path) {
            Ok(content) => {
                toml::from_str::<AppSettings>(&content).map_err(|source| SettingsError::Parse {
                    path: config_path.clone(),
                    source,
                })?
            }
            Err(source) if source.kind() == io::ErrorKind::NotFound => {
                let settings = AppSettings::defaults(default_data_root);
                write_settings(&config_path, &settings)?;
                settings
            }
            Err(source) => {
                return Err(SettingsError::Read {
                    path: config_path,
                    source,
                });
            }
        };

        settings.validate()?;

        Ok(Self {
            config_path,
            settings: RwLock::new(settings),
        })
    }

    pub(crate) fn snapshot(&self) -> AppSettings {
        self.settings
            .read()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .clone()
    }

    pub(crate) fn config_path(&self) -> &Path {
        &self.config_path
    }

    pub(crate) fn update_data_root(&self, data_root: PathBuf) -> Result<(), SettingsError> {
        let mut settings = self
            .settings
            .write()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        let previous = settings.data_root.clone();
        settings.data_root = data_root;
        if let Err(error) = settings
            .validate()
            .and_then(|()| write_settings(&self.config_path, &settings))
        {
            settings.data_root = previous;
            return Err(error);
        }
        Ok(())
    }

    pub(crate) fn update_launcher(
        &self,
        shortcut: String,
        timeout_ms: u64,
    ) -> Result<(), SettingsError> {
        let mut settings = self
            .settings
            .write()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        let previous = settings.launcher.clone();
        settings.launcher = LauncherSettings {
            shortcut,
            timeout_ms,
        };
        if let Err(error) = settings
            .validate()
            .and_then(|()| write_settings(&self.config_path, &settings))
        {
            settings.launcher = previous;
            return Err(error);
        }
        Ok(())
    }

    pub(crate) fn update_autostart(&self, autostart: bool) -> Result<(), SettingsError> {
        let mut settings = self
            .settings
            .write()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        settings.app.autostart = autostart;
        write_settings(&self.config_path, &settings)
    }
}

fn create_app_directories(config_dir: &Path) -> Result<(), SettingsError> {
    for path in [
        config_dir.to_path_buf(),
        config_dir.join("logs"),
        config_dir.join("cache").join("clipboard"),
        config_dir.join("runtime"),
    ] {
        fs::create_dir_all(&path).map_err(|source| SettingsError::CreateDirectory {
            path: path.clone(),
            source,
        })?;
    }

    Ok(())
}

fn write_settings(path: &Path, settings: &AppSettings) -> Result<(), SettingsError> {
    let mut content = toml::to_string_pretty(settings)?;
    content.push('\n');

    atomic_write(path, content.as_bytes()).map_err(|source| SettingsError::Write {
        path: path.to_path_buf(),
        source,
    })
}

#[derive(Debug, Error)]
pub(crate) enum SettingsError {
    #[error("failed to create application directory {path}: {source}")]
    CreateDirectory { path: PathBuf, source: io::Error },
    #[error("failed to read settings from {path}: {source}")]
    Read { path: PathBuf, source: io::Error },
    #[error("failed to parse settings from {path}: {source}")]
    Parse {
        path: PathBuf,
        source: toml::de::Error,
    },
    #[error("failed to serialize settings: {0}")]
    Serialize(#[from] toml::ser::Error),
    #[error("failed to write settings to {path}: {source}")]
    Write { path: PathBuf, source: io::Error },
    #[error("unsupported settings schema version {0}")]
    UnsupportedSchema(u32),
    #[error("invalid settings: {0}")]
    InvalidConfig(String),
}

#[cfg(test)]
mod tests {
    use std::{fs, path::PathBuf};

    use super::{AppSettings, SettingsError, SettingsService};

    fn temporary_directory(test_name: &str) -> PathBuf {
        let path = std::env::temp_dir().join(format!("juju-{test_name}-{}", std::process::id()));
        let _ = fs::remove_dir_all(&path);
        path
    }

    #[test]
    fn creates_default_config_and_application_directories() {
        let directory = temporary_directory("default-config");
        let data_root = PathBuf::from(r"C:\Users\tester\.juju");

        let service = SettingsService::load_or_create(directory.clone(), data_root.clone())
            .expect("default config should be created");

        assert_eq!(service.snapshot().data_root, data_root);
        assert_eq!(service.snapshot().launcher.timeout_ms, 2_000);
        assert_eq!(service.snapshot().storage.autosave_debounce_ms, 800);
        assert_eq!(service.config_path(), directory.join("config.toml"));
        assert!(directory.join("logs").is_dir());
        assert!(directory.join("cache").join("clipboard").is_dir());
        assert!(directory.join("runtime").is_dir());

        let persisted =
            fs::read_to_string(directory.join("config.toml")).expect("config should be readable");
        let parsed: AppSettings = toml::from_str(&persisted).expect("config should be valid TOML");
        assert_eq!(parsed, service.snapshot());

        fs::remove_dir_all(directory).expect("test directory should be removed");
    }

    #[test]
    fn loads_existing_config() {
        let directory = temporary_directory("existing-config");
        fs::create_dir_all(&directory).expect("test directory should be created");
        fs::write(
            directory.join("config.toml"),
            r#"schema_version = 1
data_root = "C:\\data\\juju"

[app]
autostart = true

[launcher]
timeout_ms = 3500
shortcut = "Ctrl+Alt+J"

[storage]
watch_external_changes = false
autosave_debounce_ms = 1200
"#,
        )
        .expect("fixture should be written");

        let service =
            SettingsService::load_or_create(directory.clone(), PathBuf::from(r"C:\ignored"))
                .expect("existing config should load");

        let settings = service.snapshot();
        assert_eq!(settings.data_root, PathBuf::from(r"C:\data\juju"));
        assert!(settings.app.autostart);
        assert_eq!(settings.launcher.timeout_ms, 3_500);
        assert!(!settings.storage.watch_external_changes);

        fs::remove_dir_all(directory).expect("test directory should be removed");
    }

    #[test]
    fn rejects_unsupported_schema() {
        let directory = temporary_directory("unsupported-schema");
        fs::create_dir_all(&directory).expect("test directory should be created");
        fs::write(
            directory.join("config.toml"),
            r#"schema_version = 99
data_root = "C:\\data\\juju"

[app]
autostart = false

[launcher]
timeout_ms = 2000
shortcut = "Ctrl+Shift+Alt+Space"

[storage]
watch_external_changes = true
autosave_debounce_ms = 800
"#,
        )
        .expect("fixture should be written");

        let error =
            SettingsService::load_or_create(directory.clone(), PathBuf::from(r"C:\ignored"))
                .err()
                .expect("unsupported schema should fail");
        assert!(matches!(error, SettingsError::UnsupportedSchema(99)));

        fs::remove_dir_all(directory).expect("test directory should be removed");
    }

    #[test]
    fn atomically_replaces_existing_config() {
        let directory = temporary_directory("replace-config");
        let data_root = PathBuf::from(r"C:\Users\tester\.juju");
        let service = SettingsService::load_or_create(directory.clone(), data_root)
            .expect("default config should be created");
        let mut updated = service.snapshot();
        updated.launcher.timeout_ms = 4_000;

        super::write_settings(service.config_path(), &updated)
            .expect("existing config should be replaced");

        let persisted =
            fs::read_to_string(service.config_path()).expect("replaced config should be readable");
        let parsed: AppSettings =
            toml::from_str(&persisted).expect("replaced config should be valid TOML");
        assert_eq!(parsed.launcher.timeout_ms, 4_000);

        fs::remove_dir_all(directory).expect("test directory should be removed");
    }
}
