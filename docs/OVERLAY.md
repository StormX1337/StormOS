# In-game overlay

The overlay is a normal Win32 window owned by the STORM OS app:

- `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` popup on its own STA
  thread — click-through, never takes focus, not shown in Alt+Tab.
- Drawn with GDI into the layered window; updates are marshalled with `WM_APP` messages at the telemetry interval.
- A global hotkey (`RegisterHotKey`, default `Ctrl+Shift+F10`) toggles visibility.

It does **not** inject into games, hook Direct3D/Vulkan, read game memory or use a kernel driver, so it is
compatible with anti-cheat systems. The trade-off: games in *exclusive fullscreen* draw over every window,
including the overlay. Use borderless/windowed fullscreen (Windows 11's *optimizations for windowed games* keeps
latency on par with exclusive fullscreen).

## Content

Selectable lines: FPS, average FPS, 1 % low, 0.1 % low, frame time (from frame capture), CPU and GPU load, RAM,
VRAM, CPU and GPU temperature, ping. Values without a source are shown as `n/a`, never as zero.

## Settings

*Overlay* page: enable, show automatically while a game runs, position (corner), X/Y offset, font size, background
opacity, metric selection and hotkey. Settings apply live; the page includes a live preview.
