//! Mainland China holiday date validation and resolution.
//!
//! Dates here are civil dates, not instants. An announcement's year identifies
//! the document, not necessarily the year of every date in that document.

use chrono::{Datelike, NaiveDate, Weekday};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::BTreeMap;
use std::collections::HashSet;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::{LazyLock, Mutex};
use std::time::Duration;
use tauri::AppHandle;

static STORAGE_LOCK: Mutex<()> = Mutex::new(());
static FETCHING: LazyLock<Mutex<HashSet<i32>>> = LazyLock::new(|| Mutex::new(HashSet::new()));

fn directory(app: &AppHandle) -> Result<PathBuf, String> {
    Ok(PathBuf::from(crate::settings::AppSettings::load(app)?.data_root).join("holidays"))
}

fn write_json(path: &Path, value: &impl Serialize) -> Result<(), String> {
    let parent = path.parent().ok_or("无效数据路径")?;
    fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    let temporary = path.with_extension("json.tmp");
    let result = (|| {
        let contents = serde_json::to_vec_pretty(value).map_err(|e| e.to_string())?;
        fs::write(&temporary, contents).map_err(|e| e.to_string())?;
        fs::rename(&temporary, path).map_err(|e| e.to_string())
    })();
    if result.is_err() {
        let _ = fs::remove_file(temporary);
    }
    result
}

fn load_overrides(dir: &Path) -> Result<BTreeMap<String, Override>, String> {
    let path = dir.join("overrides.json");
    let text = match fs::read_to_string(path) {
        Ok(text) => text,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(BTreeMap::new()),
        Err(e) => return Err(format!("无法读取手动设置：{e}")),
    };
    let overrides: BTreeMap<String, Override> =
        serde_json::from_str(&text).map_err(|e| format!("无法读取手动设置：{e}"))?;
    for date in overrides.keys() {
        parse_date(date).map_err(|e| format!("无法读取手动设置：{e}"))?;
    }
    Ok(overrides)
}

fn announcement(dir: &Path, year: i32) -> Option<Value> {
    let data = fs::read_to_string(dir.join(format!("{year}.json"))).ok()?;
    let value = serde_json::from_str(&data).ok()?;
    validate_announcement(year, &value).ok()?;
    Some(value)
}

fn announcements(dir: &Path) -> Result<BTreeMap<i32, Value>, String> {
    let entries = match fs::read_dir(dir) {
        Ok(entries) => entries,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(BTreeMap::new()),
        Err(e) => return Err(e.to_string()),
    };
    let mut documents = BTreeMap::new();
    for entry in entries {
        let entry = entry.map_err(|e| e.to_string())?;
        let name = entry.file_name();
        let name = name.to_string_lossy();
        if let Some(year) = name
            .strip_suffix(".json")
            .filter(|s| s.len() == 4)
            .and_then(|s| s.parse::<i32>().ok())
        {
            if let Some(value) = announcement(dir, year) {
                documents.insert(year, value);
            }
        }
    }
    Ok(documents)
}

fn mark_dirty(dir: &Path) -> Result<(), String> {
    write_json(&dir.join("index-dirty.json"), &true)
}

fn rebuild(dir: &Path) -> Result<BTreeMap<String, DayInfo>, String> {
    let index = build_index(&announcements(dir)?, &load_overrides(dir)?)?;
    write_json(&dir.join("index.json"), &index)?;
    match fs::remove_file(dir.join("index-dirty.json")) {
        Ok(()) => {}
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
        Err(e) => return Err(e.to_string()),
    }
    Ok(index)
}

fn current_index(dir: &Path) -> Result<BTreeMap<String, DayInfo>, String> {
    if dir.join("index-dirty.json").exists() || !dir.join("index.json").exists() {
        return rebuild(dir);
    }
    let cached: BTreeMap<String, DayInfo> = match fs::read_to_string(dir.join("index.json")) {
        Ok(text) => match serde_json::from_str(&text) {
            Ok(index) => index,
            Err(_) => return rebuild(dir),
        },
        Err(_) => return rebuild(dir),
    };
    // Files may have been damaged, removed, or repaired outside the app.
    // Never use stale official or manual entries merely because the cache parses.
    let actual = build_index(&announcements(dir)?, &load_overrides(dir)?)?;
    if cached != actual {
        mark_dirty(dir)?;
        return rebuild(dir);
    }
    Ok(cached)
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Snapshot {
    days: Vec<DayInfo>,
    overrides: BTreeMap<String, Override>,
    manual_error: Option<String>,
    has_announcement: bool,
    fetching: bool,
}

#[tauri::command]
pub fn holiday_snapshot(
    app: AppHandle,
    start: String,
    end: String,
    year: i32,
) -> Result<Snapshot, String> {
    let mut date = parse_date(&start)?;
    let end = parse_date(&end)?;
    if end < date || (end - date).num_days() > 3660 {
        return Err("日期范围无效".into());
    }
    let dir = directory(&app)?;
    let _guard = STORAGE_LOCK.lock().map_err(|e| e.to_string())?;
    let loaded = load_overrides(&dir);
    let manual_error = loaded.as_ref().err().cloned();
    let overrides = loaded.unwrap_or_default();
    let index = if manual_error.is_some() {
        // Do not overwrite a previously valid index with incomplete manual data.
        // If the file is repaired later, the next read must rebuild the index.
        let _ = mark_dirty(&dir);
        build_index(&announcements(&dir)?, &BTreeMap::new())?
    } else {
        current_index(&dir)?
    };
    let mut days = Vec::new();
    loop {
        days.push(resolve_day(&date.format("%Y-%m-%d").to_string(), &index)?);
        if date == end {
            break;
        }
        date = date.succ_opt().ok_or("日期超出范围")?;
    }
    Ok(Snapshot {
        days,
        overrides,
        manual_error,
        has_announcement: announcement(&dir, year).is_some(),
        fetching: FETCHING.lock().map_err(|e| e.to_string())?.contains(&year),
    })
}

#[tauri::command]
pub fn holiday_set_overrides(
    app: AppHandle,
    dates: Vec<String>,
    adjustment: Option<Override>,
) -> Result<(), String> {
    for date in &dates {
        parse_date(date)?;
    }
    let dir = directory(&app)?;
    let _guard = STORAGE_LOCK.lock().map_err(|e| e.to_string())?;
    let mut overrides = load_overrides(&dir)?;
    mark_dirty(&dir)?;
    for date in dates {
        if let Some(value) = &adjustment {
            overrides.insert(date, value.clone());
        } else {
            overrides.remove(&date);
        }
    }
    let write = write_json(&dir.join("overrides.json"), &overrides);
    let rebuilt = rebuild(&dir);
    write.and(rebuilt.map(|_| ()))
}

struct FetchGuard(i32);
impl FetchGuard {
    fn acquire(year: i32) -> Result<Self, String> {
        let mut fetching = FETCHING.lock().map_err(|e| e.to_string())?;
        if !fetching.insert(year) {
            return Err("该年度正在获取".into());
        }
        Ok(Self(year))
    }
}
impl Drop for FetchGuard {
    fn drop(&mut self) {
        if let Ok(mut fetching) = FETCHING.lock() {
            fetching.remove(&self.0);
        }
    }
}

fn finish_download(dir: &Path, year: i32, value: Option<Value>) -> Result<(), String> {
    mark_dirty(dir)?;
    let saved = match value {
        Some(value) => write_json(&dir.join(format!("{year}.json")), &value),
        None => Err("无法获取有效的官方公告".into()),
    };
    let rebuilt = rebuild(dir);
    saved.and(rebuilt.map(|_| ()))
}

fn prepare_automatic(dir: &Path, year: i32, today: NaiveDate) -> Result<bool, String> {
    if announcement(dir, year).is_some() {
        return Ok(false);
    }
    let path = dir.join("sync-state.json");
    let mut state: BTreeMap<String, BTreeMap<String, String>> = match fs::read_to_string(&path) {
        Ok(text) => serde_json::from_str(&text).map_err(|e| format!("同步状态损坏：{e}"))?,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => BTreeMap::new(),
        Err(e) => return Err(e.to_string()),
    };
    let today = today.format("%Y-%m-%d").to_string();
    let dates = state.entry("lastCheckDates".into()).or_default();
    if dates.get(&year.to_string()) == Some(&today) {
        return Ok(false);
    }
    dates.insert(year.to_string(), today);
    write_json(&path, &state)?;
    Ok(true)
}

async fn download_announcement(
    client: &reqwest::Client,
    year: i32,
    sources: &[String],
) -> Option<Value> {
    for url in sources {
        if let Ok(response) = client.get(url).send().await {
            if let Ok(response) = response.error_for_status() {
                if let Ok(value) = response.json::<Value>().await {
                    if validate_announcement(year, &value).is_ok() {
                        return Some(value);
                    }
                }
            }
        }
    }
    None
}

async fn fetch_year(app: &AppHandle, year: i32, automatic: bool) -> Result<(), String> {
    if !(1..=9999).contains(&year) {
        return Err("年份无效".into());
    }
    let _fetch_guard = FetchGuard::acquire(year)?;
    let dir = directory(app)?;
    let client = reqwest::Client::builder()
        .timeout(Duration::from_secs(8))
        .build()
        .map_err(|e| e.to_string())?;
    {
        let _guard = STORAGE_LOCK.lock().map_err(|e| e.to_string())?;
        if automatic && !prepare_automatic(&dir, year, chrono::Local::now().date_naive())? {
            return Ok(());
        }
        mark_dirty(&dir)?;
    }
    let sources = [
        format!("https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{year}.json"),
        format!("https://cdn.jsdelivr.net/gh/NateScarlet/holiday-cn@master/{year}.json"),
        format!("https://fastly.jsdelivr.net/gh/NateScarlet/holiday-cn@master/{year}.json"),
    ];
    let valid = download_announcement(&client, year, &sources).await;
    let _guard = STORAGE_LOCK.lock().map_err(|e| e.to_string())?;
    finish_download(&dir, year, valid)
}

#[tauri::command]
pub async fn holiday_fetch(app: AppHandle, year: i32) -> Result<(), String> {
    fetch_year(&app, year, false).await
}

fn automatic_years(today: NaiveDate) -> Vec<i32> {
    let mut years = vec![today.year()];
    if today.month() >= 11 {
        years.push(today.year() + 1);
    }
    years
}

pub fn start_background(app: &AppHandle) {
    let app = app.clone();
    tauri::async_runtime::spawn(async move {
        loop {
            let today = chrono::Local::now().date_naive();
            for year in automatic_years(today) {
                let _ = fetch_year(&app, year, true).await;
            }
            tokio::time::sleep(Duration::from_secs(30 * 60)).await;
        }
    });
}

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct Override {
    pub is_off_day: bool,
    pub remark: String,
}

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct DayInfo {
    pub date: String,
    pub is_off_day: bool,
    pub source: String,
    pub label: String,
    pub is_weekend: bool,
    pub is_manual: bool,
    /// Announcement selected for this date, even when a manual override wins.
    pub official_year: Option<i32>,
    /// All announcement years mentioning this date, for conflict diagnosis.
    pub announcement_years: Vec<i32>,
    pub official_is_off_day: Option<bool>,
}

#[derive(Clone, Debug, PartialEq, Eq)]
struct OfficialDay {
    name: String,
    is_off_day: bool,
}

fn parse_date(value: &str) -> Result<NaiveDate, String> {
    if value.len() != 10
        || value.as_bytes().get(4) != Some(&b'-')
        || value.as_bytes().get(7) != Some(&b'-')
        || !value.bytes().enumerate().all(|(i, b)| {
            if i == 4 || i == 7 {
                b == b'-'
            } else {
                b.is_ascii_digit()
            }
        })
    {
        return Err(format!("无效日期：{value}"));
    }
    NaiveDate::parse_from_str(value, "%Y-%m-%d").map_err(|_| format!("无效日期：{value}"))
}

fn validate_announcement(
    year: i32,
    value: &Value,
) -> Result<BTreeMap<String, OfficialDay>, String> {
    let object = value.as_object().ok_or("公告不是对象")?;
    if object.get("year").and_then(Value::as_i64) != Some(i64::from(year)) {
        return Err("公告年份不匹配".into());
    }
    let days = object
        .get("days")
        .and_then(Value::as_array)
        .ok_or("公告缺少日期列表")?;
    let mut result = BTreeMap::new();
    for day in days {
        let date = day
            .get("date")
            .and_then(Value::as_str)
            .ok_or("公告日期无效")?;
        parse_date(date)?;
        let name = day
            .get("name")
            .and_then(Value::as_str)
            .filter(|name| !name.trim().is_empty())
            .ok_or("公告名称无效")?;
        let is_off_day = day
            .get("isOffDay")
            .and_then(Value::as_bool)
            .ok_or("公告休息状态无效")?;
        let record = OfficialDay {
            name: name.to_owned(),
            is_off_day,
        };
        if let Some(previous) = result.insert(date.to_owned(), record.clone()) {
            if previous != record {
                return Err(format!("公告日期冲突：{date}"));
            }
        }
    }
    Ok(result)
}

/// Build an index only from validated documents and readable overrides.
pub fn build_index(
    announcements: &BTreeMap<i32, Value>,
    overrides: &BTreeMap<String, Override>,
) -> Result<BTreeMap<String, DayInfo>, String> {
    let mut official: BTreeMap<String, (i32, OfficialDay, Vec<i32>)> = BTreeMap::new();
    for (&year, document) in announcements {
        for (date, day) in validate_announcement(year, document)? {
            match official.get_mut(&date) {
                Some((selected_year, selected_day, years)) => {
                    years.push(year);
                    if year > *selected_year {
                        *selected_year = year;
                        *selected_day = day;
                    }
                }
                None => {
                    official.insert(date, (year, day, vec![year]));
                }
            }
        }
    }
    let mut index = BTreeMap::new();
    for (date, (year, day, years)) in official {
        let parsed = parse_date(&date)?;
        let weekend = matches!(parsed.weekday(), Weekday::Sat | Weekday::Sun);
        index.insert(
            date.clone(),
            DayInfo {
                date,
                is_off_day: day.is_off_day,
                source: "official".into(),
                label: if day.is_off_day {
                    day.name
                } else {
                    format!("{}补班", day.name)
                },
                is_weekend: weekend,
                is_manual: false,
                official_year: Some(year),
                announcement_years: years,
                official_is_off_day: Some(day.is_off_day),
            },
        );
    }
    for (date, adjustment) in overrides {
        let mut info = resolve_day(date, &index)?;
        info.is_off_day = adjustment.is_off_day;
        info.source = "manual".into();
        info.label.clone_from(&adjustment.remark);
        info.is_manual = true;
        index.insert(date.clone(), info);
    }
    Ok(index)
}

pub fn resolve_day(date: &str, index: &BTreeMap<String, DayInfo>) -> Result<DayInfo, String> {
    let parsed = parse_date(date)?;
    if let Some(info) = index.get(date) {
        return Ok(info.clone());
    }
    let weekend = matches!(parsed.weekday(), Weekday::Sat | Weekday::Sun);
    Ok(DayInfo {
        date: date.into(),
        is_off_day: weekend,
        source: "default".into(),
        label: if weekend { "休".into() } else { String::new() },
        is_weekend: weekend,
        is_manual: false,
        official_year: None,
        announcement_years: Vec::new(),
        official_is_off_day: None,
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    use std::io::{Read, Write};
    use std::net::TcpListener;

    #[test]
    fn validates_dates_and_document_conflicts() {
        assert!(parse_date("2024-02-29").is_ok());
        assert!(parse_date("2025-02-29").is_err());
        assert!(parse_date("2024-2-29").is_err());
        let doc = json!({"year": 2026, "days": [
            {"name":"春节","date":"2027-01-01","isOffDay":true},
            {"name":"春节","date":"2027-01-01","isOffDay":false}
        ]});
        assert!(validate_announcement(2026, &doc).is_err());
    }

    #[test]
    fn resolves_defaults_official_conflicts_and_manual_override() {
        let docs = BTreeMap::from([
            (
                2025,
                json!({"year":2025,"days":[{"name":"元旦","date":"2026-01-03","isOffDay":true}]}),
            ),
            (
                2026,
                json!({"year":2026,"days":[{"name":"元旦","date":"2026-01-03","isOffDay":false}]}),
            ),
        ]);
        let mut index = build_index(&docs, &BTreeMap::new()).unwrap();
        let info = resolve_day("2026-01-03", &index).unwrap();
        assert_eq!(info.label, "元旦补班");
        assert_eq!(info.announcement_years, vec![2025, 2026]);
        assert!(!info.is_off_day);
        assert!(resolve_day("2026-01-04", &index).unwrap().is_off_day);
        assert!(!resolve_day("2026-01-05", &index).unwrap().is_off_day);
        let overrides = BTreeMap::from([(
            "2026-01-03".into(),
            Override {
                is_off_day: true,
                remark: String::new(),
            },
        )]);
        index = build_index(&docs, &overrides).unwrap();
        let manual = resolve_day("2026-01-03", &index).unwrap();
        assert!(manual.is_off_day && manual.is_manual);
        assert_eq!(manual.label, "");
        assert_eq!(manual.official_year, Some(2026));
    }

    #[test]
    fn atomic_replacement_and_dirty_recovery_preserve_overrides() {
        let temp = tempfile::tempdir().unwrap();
        let dir = temp.path();
        let path = dir.join("overrides.json");
        let first = BTreeMap::from([(
            "2026-01-01".to_string(),
            Override {
                is_off_day: true,
                remark: "旧".into(),
            },
        )]);
        write_json(&path, &first).unwrap();
        let second = BTreeMap::from([(
            "2026-01-01".to_string(),
            Override {
                is_off_day: false,
                remark: "新".into(),
            },
        )]);
        mark_dirty(dir).unwrap();
        write_json(&path, &second).unwrap();
        let recovered = current_index(dir).unwrap();
        assert_eq!(recovered["2026-01-01"].label, "新");
        assert!(!dir.join("index-dirty.json").exists());
        assert!(!dir.join("overrides.json.tmp").exists());
    }

    #[test]
    fn corrupt_overrides_do_not_replace_index_or_original() {
        let temp = tempfile::tempdir().unwrap();
        let dir = temp.path();
        let path = dir.join("overrides.json");
        write_json(&path, &BTreeMap::<String, Override>::new()).unwrap();
        rebuild(dir).unwrap();
        let index = fs::read(dir.join("index.json")).unwrap();
        fs::write(&path, b"{broken").unwrap();
        mark_dirty(dir).unwrap();
        assert!(current_index(dir).is_err());
        assert_eq!(fs::read(&path).unwrap(), b"{broken");
        assert_eq!(fs::read(dir.join("index.json")).unwrap(), index);
        assert!(dir.join("index-dirty.json").exists());
    }

    #[test]
    fn next_year_checks_start_in_november() {
        assert_eq!(
            automatic_years(NaiveDate::from_ymd_opt(2026, 10, 31).unwrap()),
            vec![2026]
        );
        assert_eq!(
            automatic_years(NaiveDate::from_ymd_opt(2026, 11, 1).unwrap()),
            vec![2026, 2027]
        );
    }

    #[test]
    fn replacement_failure_cleans_temporary_file() {
        let temp = tempfile::tempdir().unwrap();
        let path = temp.path().join("overrides.json");
        fs::create_dir(&path).unwrap();
        assert!(write_json(&path, &true).is_err());
        assert!(path.is_dir());
        assert!(!temp.path().join("overrides.json.tmp").exists());
    }

    #[test]
    fn automatic_checks_are_persisted_before_request_and_retry_next_day() {
        let temp = tempfile::tempdir().unwrap();
        let dir = temp.path();
        let day = NaiveDate::from_ymd_opt(2026, 11, 1).unwrap();
        assert!(prepare_automatic(dir, 2027, day).unwrap());
        assert!(!prepare_automatic(dir, 2027, day).unwrap());
        assert!(prepare_automatic(dir, 2027, day.succ_opt().unwrap()).unwrap());
        let doc = json!({"year": 2027, "papers": ["notice"], "days": []});
        write_json(&dir.join("2027.json"), &doc).unwrap();
        assert!(
            !prepare_automatic(dir, 2027, day.succ_opt().unwrap().succ_opt().unwrap()).unwrap()
        );
        assert_eq!(announcement(dir, 2027), Some(doc));
    }

    #[test]
    fn in_flight_requests_are_deduplicated_per_year() {
        let first = FetchGuard::acquire(4137).unwrap();
        assert!(FetchGuard::acquire(4137).is_err());
        let other = FetchGuard::acquire(4138).unwrap();
        drop(first);
        assert!(FetchGuard::acquire(4137).is_ok());
        drop(other);
    }

    #[test]
    fn successful_and_failed_downloads_preserve_manual_data_and_rebuild() {
        let temp = tempfile::tempdir().unwrap();
        let dir = temp.path();
        let overrides = BTreeMap::from([(
            "2026-01-01".to_string(),
            Override {
                is_off_day: false,
                remark: "自定义".into(),
            },
        )]);
        write_json(&dir.join("overrides.json"), &overrides).unwrap();
        let doc = json!({"year":2026, "papers":["通知"], "extra":true, "days":[{"date":"2026-01-01","name":"元旦","isOffDay":true}]});
        finish_download(dir, 2026, Some(doc.clone())).unwrap();
        assert_eq!(announcement(dir, 2026), Some(doc.clone()));
        assert_eq!(current_index(dir).unwrap()["2026-01-01"].label, "自定义");
        assert!(finish_download(dir, 2026, None).is_err());
        assert_eq!(announcement(dir, 2026), Some(doc));
        assert_eq!(load_overrides(dir).unwrap(), overrides);
        assert_eq!(current_index(dir).unwrap()["2026-01-01"].label, "自定义");
        assert!(!dir.join("index-dirty.json").exists());
    }

    #[test]
    fn corrupt_announcement_is_not_served_from_an_old_index() {
        let temp = tempfile::tempdir().unwrap();
        let dir = temp.path();
        let doc =
            json!({"year":2026, "days":[{"date":"2026-01-01","name":"元旦","isOffDay":true}]});
        finish_download(dir, 2026, Some(doc)).unwrap();
        assert!(current_index(dir).unwrap().contains_key("2026-01-01"));
        fs::write(dir.join("2026.json"), b"broken").unwrap();
        let index = current_index(dir).unwrap();
        assert!(!index.contains_key("2026-01-01"));
        assert!(!resolve_day("2026-01-01", &index).unwrap().is_off_day);
    }

    fn download_from_test_server(responses: Vec<(&'static str, &'static str)>) -> Option<Value> {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let address = listener.local_addr().unwrap();
        let count = responses.len();
        let server = std::thread::spawn(move || {
            for (status, body) in responses {
                let (mut stream, _) = listener.accept().unwrap();
                let mut request = [0; 2048];
                let _ = stream.read(&mut request);
                write!(
                    stream,
                    "HTTP/1.1 {status}\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}",
                    body.len()
                )
                .unwrap();
            }
        });
        let urls = (0..count)
            .map(|i| format!("http://{address}/{i}"))
            .collect::<Vec<_>>();
        let client = reqwest::Client::builder()
            .timeout(Duration::from_secs(2))
            .no_proxy()
            .build()
            .unwrap();
        let result = tauri::async_runtime::block_on(download_announcement(&client, 2026, &urls));
        server.join().unwrap();
        result
    }

    #[test]
    fn download_tries_sources_in_order_and_preserves_raw_fields() {
        let valid = r#"{"year":2026,"papers":["notice"],"extra":{"keep":true},"days":[]}"#;
        let first = download_from_test_server(vec![("200 OK", valid)]);
        assert_eq!(first.unwrap()["extra"]["keep"], true);
        let second =
            download_from_test_server(vec![("404 Not Found", "missing"), ("200 OK", valid)]);
        assert_eq!(second.unwrap()["papers"][0], "notice");
        let third = download_from_test_server(vec![
            ("200 OK", "not json"),
            ("200 OK", r#"{"year":2025,"days":[]}"#),
            ("200 OK", valid),
        ]);
        assert_eq!(third.unwrap()["year"], 2026);
        assert!(
            download_from_test_server(vec![
                ("404 Not Found", ""),
                ("200 OK", "{}"),
                ("500 Internal Server Error", "")
            ])
            .is_none()
        );
    }
}
