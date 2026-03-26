use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Deserialize, Serialize, Default)]
pub struct CambridgeAudioInfo {
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub model: String,
    #[serde(default)]
    pub unit_id: String,
    #[serde(default)]
    pub api: String,
    #[serde(default)]
    pub udn: String,
}

#[derive(Debug, Clone, Deserialize, Serialize, Default)]
pub struct CambridgeAudioSource {
    #[serde(default)]
    pub id: String,
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub default_name: String,
    #[serde(default)]
    pub ui_selectable: bool,
    pub preferred_order: Option<i32>,
}

#[derive(Debug, Clone, Deserialize, Serialize, Default)]
pub struct CambridgeAudioState {
    #[serde(default)]
    pub source: String,
    #[serde(default)]
    pub power: bool,
    pub volume_percent: Option<i32>,
    pub volume_db: Option<i32>,
    #[serde(default)]
    pub mute: bool,
    #[serde(default)]
    pub pre_amp_mode: bool,
}

#[derive(Debug, Clone, Deserialize)]
pub struct SmoipMessage {
    pub path: String,
    #[serde(rename = "type", default)]
    pub msg_type: String,
    pub params: Option<serde_json::Value>,
    pub result: Option<i32>,
}
