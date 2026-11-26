use chrono::{Duration, NaiveDateTime, Timelike};
use std::cmp::Reverse;
use std::collections::BinaryHeap;

/// A scheduled reminder is identified by the item and the persisted due time.
/// Stale entries must be checked against storage before any reminder is shown.
#[derive(Clone, Debug, Eq, Ord, PartialEq, PartialOrd)]
pub struct ReminderDeadline {
    pub at: NaiveDateTime,
    pub item_id: String,
}

#[derive(Default)]
pub struct ReminderQueue(BinaryHeap<Reverse<ReminderDeadline>>);

impl ReminderQueue {
    pub fn push(&mut self, deadline: ReminderDeadline) {
        self.0.push(Reverse(deadline));
    }

    pub fn peek(&self) -> Option<&ReminderDeadline> {
        self.0.peek().map(|entry| &entry.0)
    }

    pub fn pop_due(&mut self, now: NaiveDateTime) -> Option<ReminderDeadline> {
        if self.peek().is_some_and(|entry| entry.at <= now) {
            self.0.pop().map(|entry| entry.0)
        } else {
            None
        }
    }
}

pub fn snooze_deadline(now: NaiveDateTime, default_minutes: i64) -> Option<NaiveDateTime> {
    let minutes = (default_minutes / 2).max(2);
    let rounded = now.with_second(0)?.with_nanosecond(0)?;
    rounded.checked_add_signed(Duration::try_minutes(minutes)?)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn at(value: &str) -> NaiveDateTime {
        NaiveDateTime::parse_from_str(value, "%Y-%m-%d %H:%M:%S").unwrap()
    }

    #[test]
    fn processes_deadlines_in_time_order() {
        let mut queue = ReminderQueue::default();
        for (item_id, time) in [("b", "2026-11-25 10:01:00"), ("a", "2026-11-25 10:00:00")] {
            queue.push(ReminderDeadline {
                item_id: item_id.into(),
                at: at(time),
            });
        }
        assert_eq!(
            queue.pop_due(at("2026-11-25 10:00:00")).unwrap().item_id,
            "a"
        );
        assert!(queue.pop_due(at("2026-11-25 10:00:30")).is_none());
        assert_eq!(
            queue.pop_due(at("2026-11-25 10:01:00")).unwrap().item_id,
            "b"
        );
    }

    #[test]
    fn snooze_rounds_down_and_enforces_two_minute_minimum() {
        assert_eq!(
            snooze_deadline(at("2026-11-25 10:05:47"), 3),
            Some(at("2026-11-25 10:07:00"))
        );
        assert_eq!(
            snooze_deadline(at("2026-11-25 10:05:47"), 10),
            Some(at("2026-11-25 10:10:00"))
        );
    }
}
