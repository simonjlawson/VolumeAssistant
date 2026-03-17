#![windows_subsystem = "windows"]
#![allow(non_snake_case, dead_code, unused_variables, unused_imports)]

mod config;
mod coordinator;
mod audio;
mod cambridge;
mod matter;
mod mdns;
mod tray;
mod window;
mod icon;

use std::sync::{Arc, Mutex};
use windows_sys::Win32::System::Com::{CoInitializeEx, COINIT_MULTITHREADED};
use windows_sys::Win32::UI::WindowsAndMessaging::{
    GetMessageW, TranslateMessage, DispatchMessageW, MSG,
};

fn main() {
    unsafe {
        CoInitializeEx(std::ptr::null(), COINIT_MULTITHREADED as u32);
    }

    let config = config::AppConfig::load();
    let state = Arc::new(Mutex::new(coordinator::AppState::new()));

    let audio = Arc::new(Mutex::new(audio::AudioController::new()));

    let ca_config = config.cambridge_audio.clone();
    let state_ca = Arc::clone(&state);
    let audio_ca = Arc::clone(&audio);
    if ca_config.enable {
        let host = ca_config.host.clone();
        let port = ca_config.port;
        let zone = ca_config.zone.clone();
        std::thread::spawn(move || {
            cambridge::run_client(host, port, zone, state_ca, audio_ca);
        });
    }

    if config.matter.enabled {
        let state_m = Arc::clone(&state);
        std::thread::spawn(move || {
            matter::server::run_server(state_m);
        });
        let discriminator = config.matter.discriminator;
        std::thread::spawn(move || {
            mdns::advertise(discriminator);
        });
    }

    tray::run_tray(Arc::clone(&audio), Arc::clone(&state));
}
