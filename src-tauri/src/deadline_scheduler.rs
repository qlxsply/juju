//! In-memory, one-shot deadline scheduling. Business data remains with callers.
use chrono::{DateTime, Utc};
use std::cmp::Reverse;
use std::collections::{BinaryHeap, HashMap};
use std::sync::{Arc, Condvar, Mutex};
use std::thread;
use std::time::Duration;

type Key = (String, String);
type Callback = Arc<dyn Fn(String) + Send + Sync + 'static>;

#[derive(Clone, Eq, Ord, PartialEq, PartialOrd)]
struct Deadline {
    at: DateTime<Utc>,
    generation: u64,
    key: Key,
}

struct Task {
    generation: u64,
    callback: Callback,
}

#[derive(Default)]
struct Queue {
    heap: BinaryHeap<Reverse<Deadline>>,
    tasks: HashMap<Key, Task>,
    generation: u64,
    shutdown: bool,
}

impl Queue {
    fn discard_stale(&mut self) {
        while self.heap.peek().is_some_and(|Reverse(entry)| {
            !self
                .tasks
                .get(&entry.key)
                .is_some_and(|task| task.generation == entry.generation)
        }) {
            self.heap.pop();
        }
    }
}

/// Tasks are keyed by namespace and business ID. Re-registering replaces a task.
pub struct DeadlineScheduler {
    shared: Arc<(Mutex<Queue>, Condvar)>,
}

impl DeadlineScheduler {
    pub fn new() -> Self {
        let shared = Arc::new((Mutex::new(Queue::default()), Condvar::new()));
        let worker = Arc::clone(&shared);
        thread::spawn(move || run(worker));
        Self { shared }
    }

    pub fn schedule<F>(
        &self,
        at: DateTime<Utc>,
        namespace: &str,
        id: &str,
        callback: F,
    ) -> Result<(), String>
    where
        F: Fn(String) + Send + Sync + 'static,
    {
        let (lock, wake) = &*self.shared;
        let mut queue = lock.lock().map_err(|_| "定时调度器不可用。".to_string())?;
        queue.generation = queue
            .generation
            .checked_add(1)
            .ok_or("定时调度器版本溢出。")?;
        let generation = queue.generation;
        let key = (namespace.to_owned(), id.to_owned());
        queue.tasks.insert(
            key.clone(),
            Task {
                generation,
                callback: Arc::new(callback),
            },
        );
        queue.heap.push(Reverse(Deadline {
            at,
            generation,
            key,
        }));
        wake.notify_one();
        Ok(())
    }

    pub fn cancel(&self, namespace: &str, id: &str) -> Result<bool, String> {
        let (lock, wake) = &*self.shared;
        let mut queue = lock.lock().map_err(|_| "定时调度器不可用。".to_string())?;
        let removed = queue
            .tasks
            .remove(&(namespace.to_owned(), id.to_owned()))
            .is_some();
        wake.notify_one();
        Ok(removed)
    }
}

fn run(shared: Arc<(Mutex<Queue>, Condvar)>) {
    let (lock, wake) = &*shared;
    let Ok(mut queue) = lock.lock() else { return };
    loop {
        if queue.shutdown {
            return;
        }
        queue.discard_stale();
        if let Some(Reverse(entry)) = queue.heap.peek() {
            let delay = entry.at.signed_duration_since(Utc::now());
            if delay <= chrono::Duration::zero() {
                let Reverse(entry) = queue.heap.pop().expect("peeked deadline");
                if let Some(task) = queue.tasks.remove(&entry.key) {
                    // A slow or panicking callback must not block the scheduler.
                    thread::spawn(move || (task.callback)(entry.key.1));
                }
                continue;
            }
            // Periodically recalculate wall-clock deadlines after clock changes or sleep.
            let timeout = delay
                .to_std()
                .unwrap_or(Duration::from_secs(30))
                .min(Duration::from_secs(30));
            let Ok((next, _)) = wake.wait_timeout(queue, timeout) else {
                return;
            };
            queue = next;
        } else {
            let Ok(next) = wake.wait(queue) else { return };
            queue = next;
        }
    }
}

impl Drop for DeadlineScheduler {
    fn drop(&mut self) {
        let (lock, wake) = &*self.shared;
        if let Ok(mut queue) = lock.lock() {
            queue.shutdown = true;
            wake.notify_one();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::mpsc;

    #[test]
    fn wakes_for_earlier_task_and_replaces_and_cancels() {
        let scheduler = DeadlineScheduler::new();
        let (tx, rx) = mpsc::channel();
        let late = Utc::now() + chrono::Duration::seconds(10);
        scheduler
            .schedule(late, "test", "replace", |_| panic!("stale callback"))
            .unwrap();
        scheduler
            .schedule(late, "test", "cancel", |_| panic!("cancelled callback"))
            .unwrap();
        assert!(scheduler.cancel("test", "cancel").unwrap());
        assert!(!scheduler.cancel("test", "cancel").unwrap());
        scheduler
            .schedule(Utc::now(), "test", "replace", move |id| {
                tx.send(id).unwrap();
            })
            .unwrap();
        assert_eq!(rx.recv_timeout(Duration::from_secs(2)).unwrap(), "replace");
    }

    #[test]
    fn namespaces_are_independent() {
        let scheduler = DeadlineScheduler::new();
        let (tx, rx) = mpsc::channel();
        for namespace in ["a", "b"] {
            let tx = tx.clone();
            scheduler
                .schedule(Utc::now(), namespace, "same", move |id| {
                    tx.send(id).unwrap();
                })
                .unwrap();
        }
        assert_eq!(rx.recv_timeout(Duration::from_secs(2)).unwrap(), "same");
        assert_eq!(rx.recv_timeout(Duration::from_secs(2)).unwrap(), "same");
    }

    #[test]
    fn queue_orders_deadlines_and_discards_stale_entries() {
        let mut queue = Queue::default();
        let now = Utc::now();
        let callback: Callback = Arc::new(|_| {});
        let key = ("tool".to_string(), "item".to_string());
        queue.tasks.insert(
            key.clone(),
            Task {
                generation: 2,
                callback,
            },
        );
        queue.heap.push(Reverse(Deadline {
            at: now,
            generation: 1,
            key: key.clone(),
        }));
        queue.heap.push(Reverse(Deadline {
            at: now + chrono::Duration::seconds(10),
            generation: 2,
            key,
        }));
        queue.discard_stale();
        assert_eq!(queue.heap.peek().unwrap().0.generation, 2);
        assert_eq!(
            queue.heap.peek().unwrap().0.at,
            now + chrono::Duration::seconds(10)
        );
    }
}
