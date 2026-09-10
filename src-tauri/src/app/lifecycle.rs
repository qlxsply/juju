use std::sync::atomic::{AtomicBool, Ordering};

#[derive(Debug, Default)]
pub(crate) struct AppLifecycle {
    is_exiting: AtomicBool,
}

impl AppLifecycle {
    pub(crate) fn is_exiting(&self) -> bool {
        self.is_exiting.load(Ordering::Acquire)
    }

    pub(crate) fn begin_exit(&self) -> bool {
        self.is_exiting
            .compare_exchange(false, true, Ordering::AcqRel, Ordering::Acquire)
            .is_ok()
    }
}

#[cfg(test)]
mod tests {
    use super::AppLifecycle;

    #[test]
    fn exit_can_only_begin_once() {
        let lifecycle = AppLifecycle::default();

        assert!(!lifecycle.is_exiting());
        assert!(lifecycle.begin_exit());
        assert!(lifecycle.is_exiting());
        assert!(!lifecycle.begin_exit());
    }
}
