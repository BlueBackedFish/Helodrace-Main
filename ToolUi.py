import tkinter as tk
from tkinter import font, ttk


PALETTE = {
    "bg": "#f4f6f8",
    "panel": "#ffffff",
    "panel_alt": "#eef2f6",
    "text": "#17202a",
    "muted": "#5e6b78",
    "border": "#c9d3df",
    "accent": "#2f6fed",
    "accent_active": "#2458c7",
    "danger": "#b42318",
    "danger_active": "#8f1d14",
    "editor_bg": "#fbfcfe",
    "editor_fg": "#15202b",
    "readonly_bg": "#eef3f8",
}


def setup_theme(root, title=None, geometry=None):
    if title:
        root.title(title)
    if geometry:
        root.geometry(geometry)
    root.configure(bg=PALETTE["bg"])

    style = ttk.Style(root)
    try:
        style.theme_use("clam")
    except tk.TclError:
        pass

    default_font = font.nametofont("TkDefaultFont")
    default_font.configure(family="Segoe UI", size=10)
    text_font = font.nametofont("TkTextFont")
    text_font.configure(family="Segoe UI", size=10)
    fixed_font = font.nametofont("TkFixedFont")
    fixed_font.configure(family="Consolas", size=10)

    style.configure(".", font=default_font, background=PALETTE["bg"], foreground=PALETTE["text"])
    style.configure("TFrame", background=PALETTE["bg"])
    style.configure("Panel.TFrame", background=PALETTE["panel"])
    style.configure("Toolbar.TFrame", background=PALETTE["panel_alt"], relief=tk.FLAT)
    style.configure("TLabel", background=PALETTE["bg"], foreground=PALETTE["text"])
    style.configure("Title.TLabel", font=("Segoe UI", 10, "bold"), foreground=PALETTE["text"])
    style.configure("Status.TLabel", font=("Consolas", 10), foreground=PALETTE["muted"])
    style.configure("TButton", padding=(10, 5))
    style.configure("Accent.TButton", foreground="#ffffff", background=PALETTE["accent"])
    style.map("Accent.TButton", background=[("active", PALETTE["accent_active"]), ("pressed", PALETTE["accent_active"])])
    style.configure("Danger.TButton", foreground="#ffffff", background=PALETTE["danger"])
    style.map("Danger.TButton", background=[("active", PALETTE["danger_active"]), ("pressed", PALETTE["danger_active"])])
    style.configure("TEntry", fieldbackground=PALETTE["panel"], padding=(6, 4))
    style.configure("TCombobox", fieldbackground=PALETTE["panel"], padding=(6, 4))
    style.configure("Treeview", background=PALETTE["panel"], fieldbackground=PALETTE["panel"], foreground=PALETTE["text"], rowheight=26)
    style.configure("Treeview.Heading", font=("Segoe UI", 10, "bold"), background=PALETTE["panel_alt"], foreground=PALETTE["text"])
    style.map("Treeview", background=[("selected", PALETTE["accent"])], foreground=[("selected", "#ffffff")])
    style.configure("TLabelframe", background=PALETTE["bg"], bordercolor=PALETTE["border"])
    style.configure("TLabelframe.Label", background=PALETTE["bg"], foreground=PALETTE["text"], font=("Segoe UI", 10, "bold"))
    style.configure("TNotebook", background=PALETTE["bg"], borderwidth=0)
    style.configure("TNotebook.Tab", padding=(12, 7))
    style.configure("Horizontal.TPanedwindow", background=PALETTE["border"])
    style.configure("Vertical.TPanedwindow", background=PALETTE["border"])

    return style


def style_text(widget, readonly=False):
    widget.configure(
        bg=PALETTE["readonly_bg"] if readonly else PALETTE["editor_bg"],
        fg=PALETTE["editor_fg"],
        insertbackground=PALETTE["accent"],
        selectbackground=PALETTE["accent"],
        selectforeground="#ffffff",
        relief=tk.SOLID,
        bd=1,
        padx=8,
        pady=7,
        highlightthickness=1,
        highlightbackground=PALETTE["border"],
        highlightcolor=PALETTE["accent"],
    )


def bind_common_shortcuts(root, save=None, reload_cmd=None, focus_filter=None):
    if save is not None:
        root.bind_all("<Control-s>", lambda _event: (save(), "break")[1])
    if reload_cmd is not None:
        root.bind_all("<Control-r>", lambda _event: (reload_cmd(), "break")[1])
    if focus_filter is not None:
        root.bind_all("<Control-f>", lambda _event: (focus_filter(), "break")[1])
