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
        .invoke_handler(tauri::generate_handler![open_directory])
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
                    Ok(()) => {
                        if let Some(window) = app_handle.get_webview_window("main") {
                            if let Ok(url) = "http://127.0.0.1:3000".parse() {
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

#[cfg(not(debug_assertions))]
fn start_production_sidecars(app: &tauri::AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    let resource_dir = app.path().resource_dir()?.join("resources");
    let app_data_dir = app.path().app_data_dir()?;
    std::fs::create_dir_all(&app_data_dir)?;
    let log_dir = app_data_dir.join("logs");
    std::fs::create_dir_all(&log_dir)?;
    let api_path = resource_dir.join("api").join(api_executable_name());
    let web_path = resource_dir.join("bin").join(web_launcher_name());

    let mut api = if api_path.exists() && !port_is_open(9101) {
        let (stdout, stderr) = log_stdio(&log_dir.join("api.log"))?;
        let mut command = Command::new(&api_path);
        command
            .current_dir(api_path.parent().ok_or("Invalid API resource path")?)
            .env("ASPNETCORE_URLS", "http://127.0.0.1:9101")
            .env("DatabaseProvider", "sqlite")
            .env("AppDatabasePath", app_data_dir.join("memorix.db"))
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
    } else {
        None
    };

    let mut web = if web_path.exists() && !port_is_open(3000) {
        let (stdout, stderr) = log_stdio(&log_dir.join("web.log"))?;
        let mut command = web_command(&web_path);
        configure_background_command(&mut command);
        Some(
            command
                .env("PORT", "3000")
                .env("HOSTNAME", "127.0.0.1")
                .stdout(stdout)
                .stderr(stderr)
                .spawn()
                .map_err(|error| {
                    std::io::Error::new(
                        error.kind(),
                        format!(
                            "Failed to start bundled Web runtime at {}: {error}",
                            web_path.display()
                        ),
                    )
                })?,
        )
    } else {
        None
    };

    if let Err(error) = wait_for_port(9101, Duration::from_secs(30))
        .and_then(|_| wait_for_port(3000, Duration::from_secs(30)))
    {
        for child in [&mut api, &mut web].into_iter().flatten() {
            let _ = child.kill();
            let _ = child.wait();
        }
        return Err(error);
    }

    let state = app.state::<Mutex<Sidecars>>();
    if let Ok(mut sidecars) = state.lock() {
        sidecars.api = api;
        sidecars.web = web;
    }
    Ok(())
}

#[cfg(not(debug_assertions))]
fn log_stdio(path: &std::path::Path) -> Result<(Stdio, Stdio), Box<dyn std::error::Error>> {
    let file = std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(path)?;
    Ok((Stdio::from(file.try_clone()?), Stdio::from(file)))
}

#[cfg(all(not(debug_assertions), target_os = "windows"))]
fn web_command(path: &std::path::Path) -> Command {
    let mut command = Command::new("cmd.exe");
    command.arg("/C").arg(path);
    command
}

#[cfg(all(not(debug_assertions), target_os = "windows"))]
fn configure_background_command(command: &mut Command) {
    const CREATE_NO_WINDOW: u32 = 0x08000000;
    command.creation_flags(CREATE_NO_WINDOW);
}

#[cfg(all(not(debug_assertions), not(target_os = "windows")))]
fn configure_background_command(_command: &mut Command) {}

#[cfg(all(not(debug_assertions), not(target_os = "windows")))]
fn web_command(path: &std::path::Path) -> Command {
    Command::new(path)
}

#[cfg(not(debug_assertions))]
fn port_is_open(port: u16) -> bool {
    std::net::TcpStream::connect(("127.0.0.1", port)).is_ok()
}

#[cfg(not(debug_assertions))]
fn wait_for_port(port: u16, timeout: Duration) -> Result<(), Box<dyn std::error::Error>> {
    let started = Instant::now();
    while started.elapsed() < timeout {
        if port_is_open(port) {
            return Ok(());
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

#[cfg(not(debug_assertions))]
fn web_launcher_name() -> &'static str {
    if cfg!(target_os = "windows") {
        "memorix-web.cmd"
    } else {
        "memorix-web"
    }
}
