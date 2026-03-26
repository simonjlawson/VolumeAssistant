pub mod models;
use models::*;

use std::sync::{Arc, Mutex};
use std::time::Duration;
use tungstenite::{connect, Message};

use crate::coordinator::AppState;
use crate::audio::AudioController;

const ENDPOINT_INFO: &str = "/system/info";
const ENDPOINT_SOURCES: &str = "/system/sources";
const ENDPOINT_ZONE_STATE: &str = "/zone/state";
const ENDPOINT_POWER: &str = "/system/power";

pub fn run_client(
    host: String,
    port: u16,
    zone: String,
    state: Arc<Mutex<AppState>>,
    audio: Arc<Mutex<AudioController>>,
) {
    let mut reconnect_delay = Duration::from_millis(500);
    let max_delay = Duration::from_millis(30000);

    loop {
        let url = format!("ws://{}:{}/smoip", host, port);
        match connect(&url) {
            Ok((mut socket, _)) => {
                reconnect_delay = Duration::from_millis(500);

                let subscribe_msg = serde_json::json!({
                    "path": format!("/zone/{}/state", zone),
                    "params": { "update": 100, "zone": zone }
                });
                let _ = socket.send(Message::text(subscribe_msg.to_string()));

                let info_req = serde_json::json!({ "path": ENDPOINT_INFO, "params": {} });
                let _ = socket.send(Message::text(info_req.to_string()));

                let sources_req = serde_json::json!({ "path": ENDPOINT_SOURCES, "params": {} });
                let _ = socket.send(Message::text(sources_req.to_string()));

                let state_req = serde_json::json!({
                    "path": ENDPOINT_ZONE_STATE,
                    "params": { "zone": zone }
                });
                let _ = socket.send(Message::text(state_req.to_string()));

                if let Ok(mut st) = state.lock() {
                    st.cambridge.connected = true;
                    st.add_log("Cambridge Audio: Connected".to_string());
                }

                loop {
                    match socket.read() {
                        Ok(Message::Text(text)) => {
                            handle_message(text.as_ref(), &state, &audio);
                        }
                        Ok(Message::Close(_)) | Err(_) => {
                            break;
                        }
                        _ => {}
                    }
                }

                if let Ok(mut st) = state.lock() {
                    st.cambridge.connected = false;
                    st.add_log("Cambridge Audio: Disconnected".to_string());
                }
            }
            Err(_) => {}
        }

        std::thread::sleep(reconnect_delay);
        reconnect_delay = (reconnect_delay * 2).min(max_delay);
    }
}

fn handle_message(
    text: &str,
    state: &Arc<Mutex<AppState>>,
    audio: &Arc<Mutex<AudioController>>,
) {
    let msg: serde_json::Value = match serde_json::from_str(text) {
        Ok(v) => v,
        Err(_) => return,
    };

    let path = msg["path"].as_str().unwrap_or("");
    let params = &msg["params"]["data"];

    if path.contains("/system/info") {
        if let Ok(info) = serde_json::from_value::<CambridgeAudioInfo>(params.clone()) {
            if let Ok(mut st) = state.lock() {
                st.cambridge.device_name = info.name;
                st.cambridge.model = info.model;
            }
        }
    } else if path.contains("/zone/state") || path.contains("zone/state") {
        if let Ok(ca_state) = serde_json::from_value::<CambridgeAudioState>(params.clone()) {
            if let Ok(mut st) = state.lock() {
                st.cambridge.source = ca_state.source.clone();
                st.cambridge.power = ca_state.power;
                st.cambridge.volume_percent = ca_state.volume_percent;
                st.cambridge.muted = ca_state.mute;
            }
            if let Some(ca_vol) = ca_state.volume_percent {
                if let Ok(audio_ctrl) = audio.lock() {
                    let win_pct = ca_vol as f32;
                    audio_ctrl.set_volume_percent(win_pct.clamp(0.0, 100.0));
                }
            }
        }
    }
}
