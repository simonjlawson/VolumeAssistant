use mdns_sd::{ServiceDaemon, ServiceInfo};

pub fn advertise(discriminator: u16) {
    let daemon = match ServiceDaemon::new() {
        Ok(d) => d,
        Err(_) => return,
    };

    let service_type = "_matterc._udp.local.";
    let instance_name = format!("VolumeAssistant-{}", discriminator);
    let host = "VolumeAssistant.local.";
    let port = 5540u16;

    let d_str = discriminator.to_string();
    let properties = vec![
        ("D", d_str.as_str()),
        ("CM", "1"),
        ("DT", "257"),
        ("DN", "VolumeAssistant"),
        ("PI", ""),
        ("PH", "33"),
    ];

    if let Ok(info) = ServiceInfo::new(
        service_type,
        &instance_name,
        host,
        "",
        port,
        properties.as_slice(),
    ) {
        let _ = daemon.register(info);
    }

    loop {
        std::thread::sleep(std::time::Duration::from_secs(60));
    }
}
