use thiserror::Error;

#[derive(Clone, Copy, Debug, Eq, Hash, PartialEq)]
pub(crate) enum ToolId {
    Json,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
#[allow(dead_code)] // Future tools may support a background runtime.
pub(crate) enum BackgroundCapability {
    Unsupported,
    Optional,
    Required,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
#[allow(dead_code)] // Letter shortcuts are reserved for future registry entries.
pub(crate) enum LauncherKey {
    Digit(u8),
    Letter(char),
}

impl LauncherKey {
    pub(crate) fn keyboard_code(self) -> String {
        match self {
            Self::Digit(digit) => format!("Digit{digit}"),
            Self::Letter(letter) => format!("Key{}", letter.to_ascii_uppercase()),
        }
    }
}

#[derive(Clone, Copy, Debug)]
pub(crate) struct WindowSpec {
    pub(crate) label: &'static str,
    pub(crate) title: &'static str,
    pub(crate) route: &'static str,
    pub(crate) default_width: u32,
    pub(crate) default_height: u32,
    pub(crate) min_width: u32,
    pub(crate) min_height: u32,
}

#[derive(Clone, Copy, Debug)]
pub(crate) struct ToolDescriptor {
    pub(crate) id: ToolId,
    pub(crate) name: &'static str,
    pub(crate) launcher_key: LauncherKey,
    #[allow(dead_code)] // Used by future Launcher search.
    pub(crate) aliases: &'static [&'static str],
    #[allow(dead_code)] // Consumed by future runtime settings.
    pub(crate) background: BackgroundCapability,
    pub(crate) window: WindowSpec,
}

const JSON_DESCRIPTOR: ToolDescriptor = ToolDescriptor {
    id: ToolId::Json,
    name: "JSON",
    launcher_key: LauncherKey::Digit(1),
    aliases: &["json", "格式化", "diff", "对比"],
    background: BackgroundCapability::Unsupported,
    window: WindowSpec {
        label: "tool-json",
        title: "JSON - juju",
        route: "index.html?view=tool&tool=json",
        default_width: 1_400,
        default_height: 900,
        min_width: 900,
        min_height: 600,
    },
};

pub(crate) struct ToolRegistry {
    descriptors: &'static [ToolDescriptor],
}

impl ToolRegistry {
    pub(crate) fn new() -> Result<Self, RegistryError> {
        let registry = Self {
            descriptors: &[JSON_DESCRIPTOR],
        };
        registry.validate()?;
        Ok(registry)
    }

    pub(crate) fn get(&self, id: ToolId) -> Option<&ToolDescriptor> {
        self.descriptors
            .iter()
            .find(|descriptor| descriptor.id == id)
    }

    fn validate(&self) -> Result<(), RegistryError> {
        for (index, descriptor) in self.descriptors.iter().enumerate() {
            if descriptor.window.label.is_empty() || descriptor.window.route.is_empty() {
                return Err(RegistryError::InvalidDescriptor(descriptor.name));
            }
            if self.descriptors[..index]
                .iter()
                .any(|other| other.id == descriptor.id)
            {
                return Err(RegistryError::DuplicateToolId(descriptor.name));
            }
            if self.descriptors[..index]
                .iter()
                .any(|other| other.launcher_key == descriptor.launcher_key)
            {
                return Err(RegistryError::DuplicateLauncherKey(
                    descriptor.launcher_key.keyboard_code(),
                ));
            }
        }
        Ok(())
    }
}

#[derive(Debug, Error)]
pub(crate) enum RegistryError {
    #[error("invalid tool descriptor for {0}")]
    InvalidDescriptor(&'static str),
    #[error("duplicate tool id for {0}")]
    DuplicateToolId(&'static str),
    #[error("duplicate launcher key {0}")]
    DuplicateLauncherKey(String),
}

#[cfg(test)]
mod tests {
    use super::{BackgroundCapability, LauncherKey, ToolId, ToolRegistry};

    #[test]
    fn json_descriptor_has_the_documented_contract() {
        let registry = ToolRegistry::new().expect("registry should be valid");
        let json = registry
            .get(ToolId::Json)
            .expect("JSON should be registered");

        assert_eq!(json.launcher_key, LauncherKey::Digit(1));
        assert_eq!(json.launcher_key.keyboard_code(), "Digit1");
        assert_eq!(json.background, BackgroundCapability::Unsupported);
        assert_eq!(json.window.label, "tool-json");
        assert_eq!(json.window.default_width, 1_400);
        assert_eq!(json.window.min_height, 600);
        assert!(json.aliases.contains(&"diff"));
    }
}
