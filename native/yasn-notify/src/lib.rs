//! Native notification bridge for YASN.
//!
//! Fronts the [`user_notify`] crate behind a minimal **synchronous** UniFFI surface so the
//! .NET app can show OS notifications over a hand-generated P/Invoke binding (AOT/trim-clean),
//! replacing the managed `OsNotifications` package whose reflection-based DBus marshalling could
//! not be annotated for NativeAOT.
//!
//! `user_notify`'s API is async (Tokio); its futures are driven to completion on a private
//! runtime here so the exported functions block and the C# side stays a plain synchronous call.
//! All errors are swallowed into a `false` return — the .NET callers are fire-and-forget and map
//! `false` to "unsupported", matching the prior senders that never threw to callers.

use std::sync::Arc;

use once_cell::sync::OnceCell;
use tokio::runtime::Runtime;
use user_notify::{NotificationBuilder, NotificationManager};

uniffi::setup_scaffolding!();

/// Private Tokio runtime used to drive `user_notify`'s async calls to completion.
static RUNTIME: OnceCell<Runtime> = OnceCell::new();

/// The platform notification manager, created once by [`init`].
static MANAGER: OnceCell<Arc<dyn NotificationManager>> = OnceCell::new();

/// Returns the shared current-thread Tokio runtime, creating it on first use.
fn runtime() -> Option<&'static Runtime> {
    RUNTIME
        .get_or_try_init(|| tokio::runtime::Builder::new_current_thread().enable_all().build())
        .map_err(|err| log::error!("failed to build tokio runtime: {err:?}"))
        .ok()
}

/// Creates and caches the platform notification manager.
///
/// `app_id` is the application identifier: on Windows it is the AppUserModelID used to obtain a
/// toast notifier; on macOS the system uses the running bundle's identifier instead (this argument
/// is effectively ignored, and the manager falls back to a no-op mock when the process is not a
/// signed `.app` bundle, e.g. under `dotnet run`); on Linux it is unused.
///
/// On macOS this also performs the first-run notification-permission request. Idempotent: repeated
/// calls return the result of the first initialization.
///
/// Returns `true` when a manager is available, `false` when the runtime could not be created.
#[uniffi::export]
pub fn init(app_id: String) -> bool {
    if MANAGER.get().is_some() {
        return true;
    }

    let Some(rt) = runtime() else {
        return false;
    };

    // get_notification_manager is synchronous and infallible: on an unsupported/unconfigured
    // platform it returns a mock that logs instead of displaying, so this never fails here.
    let manager = user_notify::get_notification_manager(app_id, None);

    // macOS requires authorization before notifications display; on other platforms this is a
    // no-op that returns Ok(true). Best-effort: a failure (e.g. not on the main thread, or denied)
    // is logged and ignored so initialization still succeeds and send attempts degrade gracefully.
    if let Err(err) = rt.block_on(manager.first_time_ask_for_notification_permission()) {
        log::warn!("notification permission request failed: {err:?}");
    }

    let _ = MANAGER.set(manager);
    true
}

/// Displays a notification with the given title and body.
///
/// `tag` maps to the platform notification "thread id" used to group related notifications; pass
/// an empty string for none. Returns `true` when the notification was handed to the OS, `false` if
/// [`init`] has not run, the runtime is unavailable, or the platform send failed.
#[uniffi::export]
pub fn show_notification(title: String, body: String, tag: String) -> bool {
    let Some(manager) = MANAGER.get() else {
        log::warn!("show_notification called before init");
        return false;
    };
    let Some(rt) = runtime() else {
        return false;
    };

    let mut builder = NotificationBuilder::new().title(&title).body(&body);
    if !tag.is_empty() {
        builder = builder.set_thread_id(&tag);
    }

    match rt.block_on(manager.send_notification(builder)) {
        Ok(_handle) => true,
        Err(err) => {
            log::error!("failed to send notification: {err:?}");
            false
        }
    }
}
