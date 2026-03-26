use std::net::UdpSocket;
use std::sync::{Arc, Mutex};
use crate::coordinator::AppState;

pub fn run_server(state: Arc<Mutex<AppState>>) {
    let socket = match UdpSocket::bind("0.0.0.0:5540") {
        Ok(s) => s,
        Err(_) => return,
    };

    let mut buf = [0u8; 1280];
    loop {
        match socket.recv_from(&mut buf) {
            Ok((size, addr)) => {
                if size < 8 {
                    continue;
                }
                let session_id = u16::from_le_bytes([buf[2], buf[3]]);
                if session_id != 0 {
                    continue;
                }
                if let Ok(mut st) = state.lock() {
                    st.add_log(format!("Matter: received {} bytes from {}", size, addr));
                }
            }
            Err(_) => break,
        }
    }
}
