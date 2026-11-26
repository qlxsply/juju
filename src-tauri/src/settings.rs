use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;
use std::fs;
use std::path::{Path, PathBuf};
use tauri::{AppHandle, Manager};

const SETTINGS_FILE: &str = "config.json";
const LEGACY_SETTINGS_FILE: &str = "app-settings.json";
const DATA_ROOT_POINTER: &str = "data-location.json";

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(default, rename_all = "camelCase")]
pub struct AppSettings {
    pub start_at_login: bool,
    pub start_minimized: bool,
    pub compact_launcher: bool,
    pub auto_hide_launcher: bool,
    pub theme: String,
    pub data_root: String,
    pub pending_data_root: Option<String>,
    pub show_tool_notifications: bool,
    pub return_to_launcher_on_tool_close: bool,
    pub launcher_timeout_milliseconds: u32,
    pub leader_shortcut: String,
    pub tool_keys: BTreeMap<String, String>,
    pub tool_global_shortcuts: BTreeMap<String, String>,
    pub tool_settings: BTreeMap<String, serde_json::Value>,
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            start_at_login: false,
            start_minimized: false,
            compact_launcher: false,
            auto_hide_launcher: true,
            theme: "system".into(),
            data_root: String::new(),
            pending_data_root: None,
            show_tool_notifications: true,
            return_to_launcher_on_tool_close: false,
            launcher_timeout_milliseconds: 2000,
            leader_shortcut: "Ctrl+Alt+Space".into(),
            tool_keys: BTreeMap::from([
                ("json".into(), "J".into()),
                ("toto".into(), "T".into()),
                ("echo".into(), "E".into()),
                ("kairos".into(), "K".into()),
                ("mysqlschema".into(), "M".into()),
                ("design-system".into(), "D".into()),
            ]),
            tool_global_shortcuts: BTreeMap::from([
                ("json".into(), "None".into()),
                ("toto".into(), "None".into()),
                ("echo".into(), "None".into()),
                ("kairos".into(), "None".into()),
            ]),
            tool_settings: BTreeMap::new(),
        }
    }
}

impl AppSettings {
    pub fn load(app: &AppHandle) -> Result<Self, String> {
        let data_root = current_data_root(app)?;
        ensure_data_root(&data_root)?;
        let path = data_root.join(SETTINGS_FILE);

        let mut settings = if path.exists() {
            let contents = fs::read_to_string(&path).map_err(|error| error.to_string())?;
            serde_json::from_str::<Self>(&contents).map_err(|error| error.to_string())?
        } else if let Some(legacy) = read_legacy_settings(app)? {
            legacy
        } else {
            Self::default()
        };

        let defaults = Self::default();
        settings
            .tool_keys
            .retain(|tool, _| defaults.tool_keys.contains_key(tool));
        for (tool, key) in defaults.tool_keys {
            settings.tool_keys.entry(tool).or_insert(key);
        }
        for (tool, shortcut) in defaults.tool_global_shortcuts {
            settings
                .tool_global_shortcuts
                .entry(tool)
                .or_insert(shortcut);
        }
        settings.data_root = data_root.to_string_lossy().into_owned();
        validate_app_settings(&settings)?;
        if let Some(pending) = &settings.pending_data_root {
            validate_pending_data_root(Path::new(pending), &data_root)?;
        }

        if !path.exists() {
            Self::save_to_path(&path, &settings)?;
        }

        Ok(settings)
    }

    pub fn save(app: &AppHandle, settings: &Self) -> Result<(), String> {
        validate_app_settings(settings)?;
        let data_root = current_data_root(app)?;
        ensure_data_root(&data_root)?;
        if let Some(pending) = &settings.pending_data_root {
            validate_pending_data_root(Path::new(pending), &data_root)?;
        }
        let mut snapshot = settings.clone();
        snapshot.data_root = data_root.to_string_lossy().into_owned();
        Self::save_to_path(&data_root.join(SETTINGS_FILE), &snapshot)
    }

    pub fn apply_pending_data_root(app: &AppHandle) -> Result<(), String> {
        let mut settings = Self::load(app)?;
        let Some(pending) = settings.pending_data_root.clone() else {
            return Ok(());
        };
        let old_root = PathBuf::from(&settings.data_root);
        let new_root = PathBuf::from(&pending);
        validate_pending_data_root(&new_root, &old_root)?;
        ensure_data_root(&new_root)?;

        settings.pending_data_root = None;
        Self::save_to_path(&old_root.join(SETTINGS_FILE), &settings)?;
        let pointer = install_directory()?.join(DATA_ROOT_POINTER);
        let contents = serde_json::to_string_pretty(&DataRootPointer {
            data_root: new_root.to_string_lossy().into_owned(),
        })
        .map_err(|error| error.to_string())?;
        if let Err(error) = write_text_atomic(&pointer, &contents) {
            settings.pending_data_root = Some(pending);
            let _ = Self::save_to_path(&old_root.join(SETTINGS_FILE), &settings);
            return Err(error);
        }
        Ok(())
    }

    fn save_to_path(path: &Path, settings: &Self) -> Result<(), String> {
        let temporary_path = path.with_extension("json.tmp");
        let contents = serde_json::to_string_pretty(settings).map_err(|error| error.to_string())?;
        fs::write(&temporary_path, contents).map_err(|error| error.to_string())?;
        fs::rename(&temporary_path, path).map_err(|error| error.to_string())
    }
}

fn validate_app_settings(settings: &AppSettings) -> Result<(), String> {
    if !(250..=30_000).contains(&settings.launcher_timeout_milliseconds) {
        return Err("主界面超时必须在 250 到 30000 毫秒之间。".into());
    }
    let keys: Vec<_> = settings.tool_keys.values().collect();
    if keys.iter().any(|key| {
        let mut characters = key.chars();
        !characters.next().is_some_and(char::is_alphanumeric) || characters.next().is_some()
    }) || keys
        .iter()
        .map(|key| key.to_lowercase())
        .collect::<std::collections::HashSet<_>>()
        .len()
        != keys.len()
    {
        return Err("各工具的单键必须是不重复的字母或数字。".into());
    }
    Ok(())
}

#[derive(Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct DataRootPointer {
    data_root: String,
}

pub fn default_data_root() -> Result<PathBuf, String> {
    Ok(install_directory()?.join("data"))
}

pub fn default_json_tool_settings() -> serde_json::Value {
    serde_json::json!({
        "autosaveDelayMilliseconds": 800,
        "indentSize": 2,
        "keepWindowOnClose": true,
        "openMaximized": false,
        "globalShortcut": "None",
        "shortcuts": {
            "Save": "Alt+S",
            "Format": "Alt+F",
            "FoldAll": "Alt+Q",
            "UnfoldAll": "Alt+W",
            "New": "Alt+A",
            "UnfoldLevel": "Alt+E",
            "CopyText": "Ctrl+C",
            "CopyMinified": "Ctrl+Shift+C",
            "CopyFile": "Ctrl+Alt+Shift+C",
            "EnterDiff": "Alt+D",
            "ExitDiff": "Escape",
            "Rename": "Alt+R",
            "Delete": "Delete"
        }
    })
}

pub fn load_json_tool_settings(app: &AppHandle) -> Result<serde_json::Value, String> {
    let root = PathBuf::from(AppSettings::load(app)?.data_root);
    let directory = root.join("tools").join("json");
    let path = directory.join("settings.json");
    if !path.exists() {
        let legacy_path = directory.join("config.json");
        if legacy_path.exists() {
            let legacy = fs::read_to_string(legacy_path).map_err(|error| error.to_string())?;
            let saved: serde_json::Value =
                serde_json::from_str(&legacy).map_err(|error| error.to_string())?;
            let migrated = merge_json_settings(default_json_tool_settings(), saved)?;
            let contents =
                serde_json::to_string_pretty(&migrated).map_err(|error| error.to_string())?;
            write_text_atomic(&path, &contents)?;
            return Ok(migrated);
        }
        fs::create_dir_all(&directory).map_err(|error| error.to_string())?;
        let defaults = default_json_tool_settings();
        let contents =
            serde_json::to_string_pretty(&defaults).map_err(|error| error.to_string())?;
        write_text_atomic(&path, &contents)?;
        return Ok(defaults);
    }
    let contents = fs::read_to_string(path).map_err(|error| error.to_string())?;
    let saved: serde_json::Value =
        serde_json::from_str(&contents).map_err(|error| error.to_string())?;
    merge_json_settings(default_json_tool_settings(), saved)
}

pub fn save_json_tool_settings(
    app: &AppHandle,
    settings: &serde_json::Value,
) -> Result<(), String> {
    validate_json_tool_settings(settings)?;
    let root = PathBuf::from(AppSettings::load(app)?.data_root);
    let directory = root.join("tools").join("json");
    fs::create_dir_all(&directory).map_err(|error| error.to_string())?;
    let path = directory.join("settings.json");
    let contents = serde_json::to_string_pretty(settings).map_err(|error| error.to_string())?;
    write_text_atomic(&path, &contents)
}

fn validate_json_tool_settings(settings: &serde_json::Value) -> Result<(), String> {
    let object = settings
        .as_object()
        .ok_or_else(|| "JSON 工具设置必须是 JSON 对象。".to_string())?;
    let range = [
        ("autosaveDelayMilliseconds", 100, 10_000),
        ("indentSize", 1, 8),
    ];
    for (field, min, max) in range {
        if !object
            .get(field)
            .and_then(serde_json::Value::as_i64)
            .is_some_and(|value| (min..=max).contains(&value))
        {
            return Err(format!("{field} 必须在 {min} 到 {max} 之间。"));
        }
    }
    let shortcuts = object
        .get("shortcuts")
        .and_then(serde_json::Value::as_object)
        .ok_or_else(|| "JSON 快捷键设置无效。".to_string())?;
    let expected = [
        "Save",
        "Format",
        "FoldAll",
        "UnfoldAll",
        "New",
        "UnfoldLevel",
        "CopyText",
        "CopyMinified",
        "CopyFile",
        "EnterDiff",
        "ExitDiff",
        "Rename",
        "Delete",
    ];
    let mut normalized = BTreeMap::new();
    for command in expected {
        let shortcut = shortcuts
            .get(command)
            .and_then(serde_json::Value::as_str)
            .filter(|shortcut| !shortcut.trim().is_empty())
            .ok_or_else(|| format!("缺少 JSON 快捷键：{command}。"))?;
        let value = shortcut
            .chars()
            .filter(|character| !character.is_whitespace())
            .collect::<String>()
            .to_lowercase();
        if [
            "alt+f4",
            "ctrl+alt+delete",
            "ctrl+alt+space",
            "ctrl+alt+shift+space",
        ]
        .contains(&value.as_str())
        {
            return Err("JSON 快捷键不能使用系统或启动器保留组合键。".into());
        }
        normalized.insert(command, value);
    }
    let contexts: [&[&str]; 4] = [
        &[
            "Save",
            "Format",
            "FoldAll",
            "UnfoldAll",
            "New",
            "UnfoldLevel",
            "CopyMinified",
            "CopyFile",
            "EnterDiff",
        ],
        &[
            "Save",
            "Format",
            "FoldAll",
            "UnfoldAll",
            "New",
            "UnfoldLevel",
            "CopyMinified",
            "CopyFile",
            "EnterDiff",
            "CopyText",
        ],
        &[
            "Save",
            "Format",
            "FoldAll",
            "UnfoldAll",
            "New",
            "UnfoldLevel",
            "CopyMinified",
            "CopyFile",
            "EnterDiff",
            "Rename",
            "Delete",
        ],
        &["ExitDiff"],
    ];
    for context in contexts {
        let values: Vec<_> = context
            .iter()
            .map(|command| normalized[*command].clone())
            .collect();
        if values
            .iter()
            .collect::<std::collections::HashSet<_>>()
            .len()
            != values.len()
        {
            return Err("同一生效上下文中的 JSON 快捷键不能重复。".into());
        }
    }
    Ok(())
}

fn merge_json_settings(
    mut defaults: serde_json::Value,
    saved: serde_json::Value,
) -> Result<serde_json::Value, String> {
    let saved = saved
        .as_object()
        .ok_or_else(|| "JSON 工具配置必须是 JSON 对象。".to_string())?;
    let defaults = defaults
        .as_object_mut()
        .ok_or_else(|| "JSON 工具默认设置无效。".to_string())?;
    for (key, value) in saved {
        if key == "shortcuts" {
            if let (Some(default_shortcuts), Some(saved_shortcuts)) = (
                defaults
                    .get_mut(key)
                    .and_then(serde_json::Value::as_object_mut),
                value.as_object(),
            ) {
                for (shortcut, value) in saved_shortcuts {
                    if default_shortcuts.contains_key(shortcut) && value.is_string() {
                        default_shortcuts.insert(shortcut.clone(), value.clone());
                    }
                }
            }
        } else if defaults.contains_key(key) {
            defaults.insert(key.clone(), value.clone());
        }
    }
    Ok(serde_json::Value::Object(defaults.clone()))
}

fn install_directory() -> Result<PathBuf, String> {
    std::env::current_exe()
        .map_err(|error| error.to_string())?
        .parent()
        .map(Path::to_path_buf)
        .ok_or_else(|| "无法定位 Juju 安装目录。".to_string())
}

fn current_data_root(app: &AppHandle) -> Result<PathBuf, String> {
    let pointer = install_directory()?.join(DATA_ROOT_POINTER);
    if pointer.exists() {
        let contents = fs::read_to_string(pointer).map_err(|error| error.to_string())?;
        let pointer: DataRootPointer =
            serde_json::from_str(&contents).map_err(|error| error.to_string())?;
        let root = PathBuf::from(pointer.data_root);
        if !root.is_absolute() {
            return Err("数据目录引导文件必须包含绝对路径。".into());
        }
        return Ok(root);
    }

    // Keep data written by the earlier Tauri preview when no install-root pointer exists yet.
    if let Some(legacy) = read_legacy_settings(app)? {
        let legacy_root = PathBuf::from(legacy.data_root);
        if legacy_root.is_absolute() && legacy_root.join("juju.json").exists() {
            return Ok(legacy_root);
        }
    }
    default_data_root()
}

fn read_legacy_settings(app: &AppHandle) -> Result<Option<AppSettings>, String> {
    let Ok(directory) = app.path().app_config_dir() else {
        return Ok(None);
    };
    let path = directory.join(LEGACY_SETTINGS_FILE);
    if !path.exists() {
        return Ok(None);
    }
    let contents = fs::read_to_string(path).map_err(|error| error.to_string())?;
    serde_json::from_str(&contents)
        .map(Some)
        .map_err(|error| error.to_string())
}

fn validate_pending_data_root(target: &Path, source: &Path) -> Result<(), String> {
    if !target.is_absolute() {
        return Err("数据目录必须是绝对路径。".into());
    }
    if let Ok(metadata) = fs::symlink_metadata(target) {
        if metadata.file_type().is_symlink() {
            return Err("数据目录不能是符号链接或联接目录。".into());
        }
    }
    let target = canonicalize_with_missing_path(target)?;
    let source = source
        .canonicalize()
        .unwrap_or_else(|_| source.to_path_buf());
    if target.starts_with(&source) && target != source {
        return Err("新目录不能位于当前目录内。".into());
    }
    if target.exists() {
        let metadata = fs::symlink_metadata(&target).map_err(|error| error.to_string())?;
        if metadata.file_type().is_symlink() {
            return Err("数据目录不能是符号链接或联接目录。".into());
        }
        let marker = target.join("juju.json");
        if marker.exists() {
            let contents = fs::read_to_string(marker).map_err(|error| error.to_string())?;
            let marker: serde_json::Value =
                serde_json::from_str(&contents).map_err(|error| error.to_string())?;
            if marker["schemaVersion"].as_u64() != Some(1) {
                return Err("数据目录标记无效或版本不受支持。".into());
            }
        } else if fs::read_dir(&target)
            .map_err(|error| error.to_string())?
            .next()
            .is_some()
        {
            return Err("目标目录必须为空或包含有效的 juju.json 标记。".into());
        }
    }
    Ok(())
}

fn canonicalize_with_missing_path(path: &Path) -> Result<PathBuf, String> {
    let mut ancestor = path;
    let mut suffix = Vec::new();
    while !ancestor.exists() {
        let name = ancestor
            .file_name()
            .ok_or_else(|| "无法解析数据目录路径。".to_string())?;
        suffix.push(name.to_os_string());
        ancestor = ancestor
            .parent()
            .ok_or_else(|| "无法解析数据目录路径。".to_string())?;
    }
    let mut resolved = ancestor.canonicalize().map_err(|error| error.to_string())?;
    for component in suffix.iter().rev() {
        resolved.push(component);
    }
    Ok(resolved)
}

fn write_text_atomic(path: &Path, contents: &str) -> Result<(), String> {
    let temporary = path.with_extension("json.tmp");
    fs::write(&temporary, contents).map_err(|error| error.to_string())?;
    fs::rename(temporary, path).map_err(|error| error.to_string())
}

fn ensure_data_root(root: &Path) -> Result<(), String> {
    if !root.is_absolute() {
        return Err("数据目录必须是绝对路径。".into());
    }
    fs::create_dir_all(root).map_err(|error| error.to_string())?;
    let metadata = fs::symlink_metadata(root).map_err(|error| error.to_string())?;
    if metadata.file_type().is_symlink() {
        return Err("数据目录不能是符号链接。".into());
    }
    let marker_path = root.join("juju.json");
    if marker_path.exists() {
        let marker = fs::read_to_string(&marker_path).map_err(|error| error.to_string())?;
        let marker: serde_json::Value =
            serde_json::from_str(&marker).map_err(|error| error.to_string())?;
        if marker["schemaVersion"].as_u64() != Some(1) {
            return Err("数据目录标记无效或版本不受支持。".into());
        }
    } else {
        let mut entries = fs::read_dir(root).map_err(|error| error.to_string())?;
        if entries.next().is_some() {
            return Err("数据目录必须为空，或包含有效的 juju.json 标记。".into());
        }
        fs::write(marker_path, "{\n  \"schemaVersion\": 1\n}\n")
            .map_err(|error| error.to_string())?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{AppSettings, ensure_data_root, validate_app_settings, validate_pending_data_root};
    use std::fs;

    #[test]
    fn settings_serialize_with_camel_case_and_plain_json_values() {
        let settings = AppSettings {
            start_at_login: true,
            data_root: r"C:\Users\Test\Juju\data".into(),
            ..AppSettings::default()
        };

        let value = serde_json::to_value(settings).expect("settings serialize");
        assert_eq!(value["startAtLogin"], true);
        assert_eq!(value["dataRoot"], r"C:\Users\Test\Juju\data");
        assert_eq!(value["theme"], "system");
    }

    #[test]
    fn initializes_an_empty_directory_with_a_versioned_marker() {
        let temporary = tempfile::tempdir().expect("temporary directory");
        let data_root = temporary.path().join("data");

        ensure_data_root(&data_root).expect("empty data directory initializes");

        let marker: serde_json::Value = serde_json::from_slice(
            &fs::read(data_root.join("juju.json")).expect("marker file exists"),
        )
        .expect("marker is JSON");
        assert_eq!(marker["schemaVersion"], 1);
    }

    #[test]
    fn rejects_nonempty_unmarked_directories() {
        let temporary = tempfile::tempdir().expect("temporary directory");
        let data_root = temporary.path().join("data");
        fs::create_dir_all(&data_root).expect("create data directory");
        fs::write(data_root.join("unrelated.txt"), "keep me").expect("write sentinel");

        assert!(ensure_data_root(&data_root).is_err());
        assert!(data_root.join("unrelated.txt").exists());
    }

    #[test]
    fn rejects_invalid_data_root_markers() {
        let temporary = tempfile::tempdir().expect("temporary directory");
        let data_root = temporary.path().join("data");
        fs::create_dir_all(&data_root).expect("create data directory");
        fs::write(data_root.join("juju.json"), "{\"schemaVersion\":0}")
            .expect("write invalid marker");

        assert!(ensure_data_root(&data_root).is_err());
    }

    #[test]
    fn rejects_data_root_nested_under_the_current_root() {
        let temporary = tempfile::tempdir().expect("temporary directory");
        let current = temporary.path().join("current");
        let nested = current.join("nested");
        fs::create_dir_all(&current).expect("create current directory");

        assert!(validate_pending_data_root(&nested, &current).is_err());
    }

    #[test]
    fn allows_an_empty_or_marked_external_data_root() {
        let temporary = tempfile::tempdir().expect("temporary directory");
        let current = temporary.path().join("current");
        let target = temporary.path().join("target");
        fs::create_dir_all(&current).expect("create current directory");
        fs::create_dir_all(&target).expect("create target directory");

        validate_pending_data_root(&target, &current).expect("empty external root is valid");
        fs::write(target.join("juju.json"), "{\"schemaVersion\":1}").expect("write valid marker");
        validate_pending_data_root(&target, &current).expect("marked external root is valid");
    }

    #[test]
    fn saves_settings_over_an_existing_config_file() {
        let temporary = tempfile::tempdir().expect("temporary directory");
        let path = temporary.path().join("config.json");
        let mut settings = AppSettings::default();
        AppSettings::save_to_path(&path, &settings).expect("write initial settings");
        settings.start_minimized = true;
        AppSettings::save_to_path(&path, &settings).expect("replace settings");

        let saved: AppSettings =
            serde_json::from_slice(&fs::read(path).expect("read saved settings"))
                .expect("saved settings are valid JSON");
        assert!(saved.start_minimized);
    }

    #[test]
    fn rejects_invalid_launcher_timeout_and_duplicate_tool_keys() {
        let mut settings = AppSettings {
            launcher_timeout_milliseconds: 249,
            ..AppSettings::default()
        };
        assert!(validate_app_settings(&settings).is_err());

        settings.launcher_timeout_milliseconds = 2000;
        settings.tool_keys.insert("json".into(), "T".into());
        assert!(validate_app_settings(&settings).is_err());
    }
}
