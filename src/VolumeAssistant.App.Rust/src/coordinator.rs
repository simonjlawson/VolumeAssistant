use std::collections::VecDeque;

#[derive(Debug, Clone, Default)]
pub struct CambridgeAudioStatus {
    pub connected: bool,
    pub device_name: String,
    pub model: String,
    pub source: String,
    pub power: bool,
    pub volume_percent: Option<i32>,
    pub muted: bool,
}

#[derive(Debug, Default)]
pub struct AppState {
    pub cambridge: CambridgeAudioStatus,
    /// Ring buffer of log entries; capped at 1 000 messages.
    pub log_entries: VecDeque<String>,
    pub windows_volume_percent: f32,
    pub windows_muted: bool,
}

impl AppState {
    pub fn new() -> Self {
        AppState::default()
    }

    pub fn add_log(&mut self, entry: String) {
        self.log_entries.push_back(entry);
        if self.log_entries.len() > 1000 {
            // pop_front is O(1) on VecDeque, unlike remove(0) on Vec
            self.log_entries.pop_front();
        }
    }
}

pub fn windows_to_cambridge_volume(windows_pct: f32, max_volume: Option<i32>) -> i32 {
    let max = max_volume.unwrap_or(100) as f32;
    ((windows_pct / 100.0) * max).round() as i32
}

pub fn cambridge_to_windows_volume(ca_pct: i32, max_volume: Option<i32>) -> f32 {
    let max = max_volume.unwrap_or(100) as f32;
    (ca_pct as f32 / max * 100.0).min(100.0)
}
