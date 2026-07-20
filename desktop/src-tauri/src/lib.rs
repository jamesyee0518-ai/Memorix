#[cfg(all(not(debug_assertions), target_os = "windows"))]
use std::os::windows::process::CommandExt;
use std::process::Child;
#[cfg(not(debug_assertions))]
use std::process::{Command, Stdio};
use std::sync::Mutex;
#[cfg(not(debug_assertions))]
use std::time::{Duration, Instant};
use tauri::Manager;

#[allow(dead_code)]
struct Sidecars {
    api: Option<Child>,
    web: Option<Child>,
}

impl Drop for Sidecars {
    fn drop(&mut self) {
        for child in [&mut self.api, &mut self.web].into_iter().flatten() {
            let _ = child.kill();
            let _ = child.wait();
        }
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let app = tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .invoke_handler(tauri::generate_handler![open_directory, open_external_url])
        .manage(Mutex::new(Sidecars {
            api: None,
            web: None,
        }))
        .setup(|app| {
            #[cfg(not(debug_assertions))]
            {
                if let Some(window) = app.get_webview_window("main") {
                    let _ = window.set_title("Memorix - 正在启动");
                }

                let app_handle = app.handle().clone();
                std::thread::spawn(move || match start_production_sidecars(&app_handle) {
                    Ok(web_port) => {
                        if let Some(window) = app_handle.get_webview_window("main") {
                            if let Ok(url) = format!("http://127.0.0.1:{web_port}").parse() {
                                let _ = window.navigate(url);
                            }
                            let _ = window.set_title("Memorix");
                        }
                    }
                    Err(error) => {
                        if let Some(window) = app_handle.get_webview_window("main") {
                            let message = serde_json::to_string(&error.to_string())
                                .unwrap_or_else(|_| "\"Unknown startup error\"".to_string());
                            let _ = window.eval(format!("window.showStartupError?.({message})"));
                            let _ = window.set_title("Memorix - 启动失败");
                        }
                    }
                });
            }

            #[cfg(debug_assertions)]
            if let Some(window) = app.get_webview_window("main") {
                let url = "http://localhost:3000".parse()?;
                window.navigate(url)?;
                let _ = window.set_title("Memorix");
            }
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("error while building Memorix desktop shell");

    app.run(|app_handle, event| {
        if matches!(
            event,
            tauri::RunEvent::ExitRequested { .. } | tauri::RunEvent::Exit
        ) {
            stop_sidecars(app_handle);
        }
    });
}

fn stop_sidecars(app: &tauri::AppHandle) {
    let state = app.state::<Mutex<Sidecars>>();
    if let Ok(mut sidecars) = state.lock() {
        stop_child(&mut sidecars.api);
        stop_child(&mut sidecars.web);
    };
}

fn stop_child(child: &mut Option<Child>) {
    if let Some(mut process) = child.take() {
        let _ = process.kill();
        let _ = process.wait();
    }
}

#[tauri::command]
fn open_directory(path: String) -> Result<(), String> {
    let directory = std::path::PathBuf::from(path);
    if !directory.is_dir() {
        return Err("目录不存在或不可访问".to_string());
    }

    #[cfg(target_os = "windows")]
    let mut command = {
        let mut command = std::process::Command::new("explorer.exe");
        command.arg(&directory);
        command
    };

    #[cfg(target_os = "macos")]
    let mut command = {
        let mut command = std::process::Command::new("open");
        command.arg(&directory);
        command
    };

    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    let mut command = {
        let mut command = std::process::Command::new("xdg-open");
        command.arg(&directory);
        command
    };

    command
        .spawn()
        .map(|_| ())
        .map_err(|error| format!("无法打开目录: {error}"))
}

#[tauri::command]
fn open_external_url(url: String) -> Result<(), String> {
    let parsed = tauri::Url::parse(&url).map_err(|_| "外部链接格式无效".to_string())?;
    if parsed.scheme() != "https" {
        return Err("仅允许打开 HTTPS 外部链接".to_string());
    }

    #[cfg(target_os = "windows")]
    let mut command = {
        let mut command = std::process::Command::new("rundll32.exe");
        command
            .arg("url.dll,FileProtocolHandler")
            .arg(parsed.as_str());
        command
    };

    #[cfg(target_os = "macos")]
    let mut command = {
        let mut command = std::process::Command::new("open");
        command.arg(parsed.as_str());
        command
    };

    #[cfg(all(not(target_os = "windows"), not(target_os = "macos")))]
    let mut command = {
        let mut command = std::process::Command::new("xdg-open");
        command.arg(parsed.as_str());
        command
    };

    command
        .spawn()
        .map(|_| ())
        .map_err(|error| format!("无法打开浏览器: {error}"))
}

#[cfg(not(debug_assertions))]
fn start_production_sidecars(app: &tauri::AppHandle) -> Result<u16, Box<dyn std::error::Error>> {
    let resource_dir = app.path().resource_dir()?.join("resources");
    let app_data_dir = app.path().app_data_dir()?;
    std::fs::create_dir_all(&app_data_dir)?;
    let log_dir = app_data_dir.join("logs");
    std::fs::create_dir_all(&log_dir)?;
    let api_path = resource_dir.join("api").join(api_executable_name());
    let web_dir = resource_dir.join("web");
    let web_server_path = web_dir.join("server.js");

    if !api_path.is_file() {
        return Err(format!("Bundled API executable is missing: {}", api_path.display()).into());
    }

    let (web_port, api_port) = find_available_port_pair()?;

    let mut api = {
        let (stdout, stderr) = log_stdio(&log_dir.join("api.log"))?;
        let mut command = Command::new(&api_path);
        command
            .current_dir(api_path.parent().ok_or("Invalid API resource path")?)
            .env("ASPNETCORE_URLS", format!("http://127.0.0.1:{api_port}"))
            .env(
                "Cors__AllowedOrigins__0",
                format!("http://127.0.0.1:{web_port}"),
            )
            .env("DatabaseProvider", "sqlite")
            .env("Authentication__EnableLocalLoopback", "true")
            .env("AppDatabasePath", app_data_dir.join("memorix.db"))
            .env("ConfigDirectory", app_data_dir.join("config"))
            .env("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE", "false")
            .stdout(stdout)
            .stderr(stderr);
        configure_background_command(&mut command);
        Some(command.spawn().map_err(|error| {
            std::io::Error::new(
                error.kind(),
                format!(
                    "Failed to start bundled API at {}: {error}",
                    api_path.display()
                ),
            )
        })?)
    };

    let web_log_path = log_dir.join("web.log");
    let mut web = {
        let (stdout, stderr) = log_stdio(&web_log_path)?;
        let mut command = web_command(&resource_dir, &web_server_path)?;
        configure_background_command(&mut command);
        Some(
            command
                .current_dir(&web_dir)
                .env("NODE_ENV", "production")
                .env("PORT", web_port.to_string())
                .env("HOSTNAME", "127.0.0.1")
                .stdout(stdout)
                .stderr(stderr)
                .spawn()
                .map_err(|error| {
                    std::io::Error::new(
                        error.kind(),
                        format!(
                            "Failed to start bundled Web runtime at {}: {error}",
                            web_server_path.display()
                        ),
                    )
                })?,
        )
    };

    if let Err(error) = wait_for_port(api_port, Duration::from_secs(30), api.as_mut(), "API")
        .and_then(|_| {
            wait_for_port(
                web_port,
                Duration::from_secs(30),
                web.as_mut(),
                "Web runtime",
            )
        })
    {
        for child in [&mut api, &mut web].into_iter().flatten() {
            let _ = child.kill();
            let _ = child.wait();
        }
        let web_log = read_log_tail(&web_log_path, 4000)
            .map(|content| format!(" Web log: {content}"))
            .unwrap_or_default();
        return Err(format!("{error}.{web_log} Log file: {}", web_log_path.display()).into());
    }

    let state = app.state::<Mutex<Sidecars>>();
    if let Ok(mut sidecars) = state.lock() {
        sidecars.api = api;
        sidecars.web = web;
    }
    Ok(web_port)
}

#[cfg(not(debug_assertions))]
fn log_stdio(path: &std::path::Path) -> Result<(Stdio, Stdio), Box<dyn std::error::Error>> {
    let file = std::fs::OpenOptions::new()
        .create(true)
        .write(true)
        .truncate(true)
        .open(path)?;
    Ok((Stdio::from(file.try_clone()?), Stdio::from(file)))
}

#[cfg(not(debug_assertions))]
fn web_command(
    resource_dir: &std::path::Path,
    server_path: &std::path::Path,
) -> Result<Command, Box<dyn std::error::Error>> {
    if !server_path.is_file() {
        return Err(format!(
            "Bundled Web entry point is missing: {}",
            server_path.display()
        )
        .into());
    }

    let bundled_node = if cfg!(target_os = "windows") {
        resource_dir.join("node").join("node.exe")
    } else {
        resource_dir.join("node").join("bin").join("node")
    };
    let node = std::env::var_os("MEMORIX_NODE_BIN")
        .map(std::path::PathBuf::from)
        .unwrap_or(bundled_node);

    if !node.is_file() {
        return Err(format!("Bundled Node runtime is missing: {}", node.display()).into());
    }

    let mut command = Command::new(node);
    command.arg("server.js");
    Ok(command)
}

#[cfg(all(not(debug_assertions), target_os = "windows"))]
fn configure_background_command(command: &mut Command) {
    const CREATE_NO_WINDOW: u32 = 0x08000000;
    command.creation_flags(CREATE_NO_WINDOW);
}

#[cfg(all(not(debug_assertions), not(target_os = "windows")))]
fn configure_background_command(_command: &mut Command) {}

#[cfg(not(debug_assertions))]
fn read_log_tail(path: &std::path::Path, max_bytes: usize) -> Option<String> {
    let content = std::fs::read(path).ok()?;
    let start = content.len().saturating_sub(max_bytes);
    let text = String::from_utf8_lossy(&content[start..])
        .trim()
        .to_string();
    (!text.is_empty()).then_some(text)
}

#[cfg(not(debug_assertions))]
fn port_is_open(port: u16) -> bool {
    std::net::TcpStream::connect(("127.0.0.1", port)).is_ok()
}

#[cfg(not(debug_assertions))]
fn find_available_port_pair() -> Result<(u16, u16), Box<dyn std::error::Error>> {
    for web_port in (43120..=43218).step_by(2) {
        let api_port = web_port + 1;
        if let (Ok(web_listener), Ok(api_listener)) = (
            std::net::TcpListener::bind(("127.0.0.1", web_port)),
            std::net::TcpListener::bind(("127.0.0.1", api_port)),
        ) {
            drop(web_listener);
            drop(api_listener);
            return Ok((web_port, api_port));
        }
    }
    Err("Memorix could not find two available local ports".into())
}

#[cfg(not(debug_assertions))]
fn wait_for_port(
    port: u16,
    timeout: Duration,
    mut child: Option<&mut Child>,
    service_name: &str,
) -> Result<(), Box<dyn std::error::Error>> {
    let started = Instant::now();
    while started.elapsed() < timeout {
        if port_is_open(port) {
            return Ok(());
        }
        if let Some(process) = child.as_mut() {
            if let Some(status) = process.try_wait()? {
                return Err(format!(
                    "Memorix {service_name} exited with {status} before port {port} became ready"
                )
                .into());
            }
        }
        std::thread::sleep(Duration::from_millis(200));
    }

    Err(format!("Memorix local service on port {port} did not become ready").into())
}

#[cfg(not(debug_assertions))]
fn api_executable_name() -> &'static str {
    if cfg!(target_os = "windows") {
        "KnowledgeEngine.Api.exe"
    } else {
        "KnowledgeEngine.Api"
    }
}
