use std::collections::HashMap;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tauri::async_runtime::JoinHandle;
use tauri::{AppHandle, Emitter};
use tauri_plugin_notification::NotificationExt;

static NEXT_TASK_ID: AtomicU64 = AtomicU64::new(1);

#[derive(Default)]
pub struct TaskScheduler {
    tasks: Arc<Mutex<HashMap<String, JoinHandle<()>>>>,
}

impl TaskScheduler {
    pub fn schedule_periodic<F>(
        &self,
        id: impl Into<String>,
        app: AppHandle,
        interval: Duration,
        work: F,
    ) -> Result<(), String>
    where
        F: Fn(&AppHandle) + Send + Sync + 'static,
    {
        let id = id.into();
        let work = Arc::new(work);
        let handle = tauri::async_runtime::spawn(async move {
            loop {
                work(&app);
                tokio::time::sleep(interval).await;
            }
        });
        let previous = self
            .tasks
            .lock()
            .map_err(|_| "任务调度器不可用。".to_string())?
            .insert(id, handle);
        if let Some(previous) = previous {
            previous.abort();
        }
        Ok(())
    }

    pub fn schedule_demo(&self, app: AppHandle, delay_ms: u64) -> Result<String, String> {
        let id = format!("demo-{}", NEXT_TASK_ID.fetch_add(1, Ordering::Relaxed));
        let task_id = id.clone();
        let tasks = Arc::clone(&self.tasks);
        let handle = tauri::async_runtime::spawn(async move {
            tokio::time::sleep(Duration::from_millis(delay_ms.max(100))).await;
            let _ = app
                .notification()
                .builder()
                .title("Juju 后台任务")
                .body("演示任务已到期。")
                .show();
            let _ = app.emit("scheduler:task-fired", task_id.clone());
            if let Ok(mut tasks) = tasks.lock() {
                tasks.remove(&task_id);
            }
        });

        self.tasks
            .lock()
            .map_err(|_| "任务调度器不可用。".to_string())?
            .insert(id.clone(), handle);
        Ok(id)
    }

    pub fn cancel(&self, id: &str) -> Result<bool, String> {
        let handle = self
            .tasks
            .lock()
            .map_err(|_| "任务调度器不可用。".to_string())?
            .remove(id);
        if let Some(handle) = handle {
            handle.abort();
            Ok(true)
        } else {
            Ok(false)
        }
    }
}

impl Drop for TaskScheduler {
    fn drop(&mut self) {
        if let Ok(mut tasks) = self.tasks.lock() {
            for (_, handle) in tasks.drain() {
                handle.abort();
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::TaskScheduler;

    #[test]
    fn cancelling_unknown_task_is_a_safe_noop() {
        assert!(!TaskScheduler::default().cancel("missing-task").unwrap());
    }
}
