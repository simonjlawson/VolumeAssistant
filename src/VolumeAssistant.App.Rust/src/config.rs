use serde::{Deserialize, Serialize};
use std::fs;

#[derive(Debug, Clone, Deserialize, Serialize)]
pub struct CambridgeAudioConfig {
    #[serde(rename = "Enable", default)]
    pub enable: bool,
    #[serde(rename = "Host", default)]
    pub host: String,
    #[serde(rename = "Port", default = "default_port")]
    pub port: u16,
    #[serde(rename = "Zone", default = "default_zone")]
    pub zone: String,
    #[serde(rename = "RelativeVolume", default)]
    pub relative_volume: bool,
    #[serde(rename = "MaxVolume")]
    pub max_volume: Option<i32>,
    #[serde(rename = "MediaKeysEnabled", default)]
    pub media_keys_enabled: bool,
    #[serde(rename = "SourceSwitchingEnabled", default)]
    pub source_switching_enabled: bool,
    #[serde(rename = "SourceSwitchingNames", default)]
    pub source_switching_names: String,
    #[serde(rename = "InitialReconnectDelayMs", default = "default_reconnect_delay")]
    pub initial_reconnect_delay_ms: u64,
    #[serde(rename = "MaxReconnectDelayMs", default = "default_max_reconnect_delay")]
    pub max_reconnect_delay_ms: u64,
    #[serde(rename = "StartVolume")]
    pub start_volume: Option<i32>,
    #[serde(rename = "StartSourceName", default)]
    pub start_source_name: String,
    #[serde(rename = "StartPower", default)]
    pub start_power: bool,
    #[serde(rename = "ClosePower", default)]
    pub close_power: bool,
    #[serde(rename = "StartPowerOnVolumeChange", default)]
    pub start_power_on_volume_change: bool,
}

impl Default for CambridgeAudioConfig {
    fn default() -> Self {
        Self {
            enable: false,
            host: String::new(),
            port: 80,
            zone: "ZONE1".to_string(),
            relative_volume: false,
            max_volume: None,
            media_keys_enabled: false,
            source_switching_enabled: false,
            source_switching_names: String::new(),
            initial_reconnect_delay_ms: 500,
            max_reconnect_delay_ms: 30000,
            start_volume: None,
            start_source_name: String::new(),
            start_power: false,
            close_power: false,
            start_power_on_volume_change: false,
        }
    }
}

#[derive(Debug, Clone, Deserialize, Serialize)]
pub struct MatterConfig {
    #[serde(rename = "Enabled", default)]
    pub enabled: bool,
    #[serde(rename = "Discriminator", default = "default_discriminator")]
    pub discriminator: u16,
    #[serde(rename = "VendorId", default = "default_vendor_id")]
    pub vendor_id: u16,
    #[serde(rename = "ProductId", default = "default_product_id")]
    pub product_id: u16,
    #[serde(rename = "Passcode", default = "default_passcode")]
    pub passcode: u32,
}

impl Default for MatterConfig {
    fn default() -> Self {
        Self {
            enabled: false,
            discriminator: 3840,
            vendor_id: 65521,
            product_id: 32768,
            passcode: 20202021,
        }
    }
}

#[derive(Debug, Clone, Deserialize, Serialize, Default)]
pub struct AppConfig2 {
    #[serde(rename = "UseSourcePopup", default = "default_true")]
    pub use_source_popup: bool,
}

/// Matches the `"VolumeAssistant": { "Matter": {...} }` nesting in appsettings.json.
#[derive(Debug, Clone, Deserialize, Serialize, Default)]
pub struct VolumeAssistantSection {
    #[serde(rename = "Matter", default)]
    pub matter: MatterConfig,
}

#[derive(Debug, Clone, Deserialize, Serialize, Default)]
pub struct AppConfig {
    #[serde(rename = "CambridgeAudio", default)]
    pub cambridge_audio: CambridgeAudioConfig,
    /// Matter config lives under the `VolumeAssistant` key in appsettings.json.
    #[serde(rename = "VolumeAssistant", default)]
    pub volume_assistant: VolumeAssistantSection,
    #[serde(rename = "App", default)]
    pub app: AppConfig2,
}

fn default_port() -> u16 { 80 }
fn default_zone() -> String { "ZONE1".to_string() }
fn default_reconnect_delay() -> u64 { 500 }
fn default_max_reconnect_delay() -> u64 { 30000 }
fn default_discriminator() -> u16 { 3840 }
fn default_vendor_id() -> u16 { 65521 }
fn default_product_id() -> u16 { 32768 }
fn default_passcode() -> u32 { 20202021 }
fn default_true() -> bool { true }

impl AppConfig {
    /// Convenience accessor so callers can use `config.matter()` instead of
    /// `config.volume_assistant.matter`.
    pub fn matter(&self) -> &MatterConfig {
        &self.volume_assistant.matter
    }

    pub fn load() -> Self {
        if let Ok(exe_path) = std::env::current_exe() {
            if let Some(dir) = exe_path.parent() {
                let path = dir.join("appsettings.json");
                if let Ok(data) = fs::read_to_string(&path) {
                    if let Ok(cfg) = serde_json::from_str(&data) {
                        return cfg;
                    }
                }
            }
        }
        if let Ok(data) = fs::read_to_string("appsettings.json") {
            if let Ok(cfg) = serde_json::from_str(&data) {
                return cfg;
            }
        }
        AppConfig::default()
    }

    pub fn save(&self) {
        if let Ok(exe_path) = std::env::current_exe() {
            if let Some(dir) = exe_path.parent() {
                let path = dir.join("appsettings.json");
                if let Ok(json) = serde_json::to_string_pretty(self) {
                    let _ = fs::write(path, json);
                }
            }
        }
    }
}
