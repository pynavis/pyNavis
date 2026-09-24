"""The Navisworks application: documents, GUI, versions."""

import clr

from pynavis._api import Api, Application


def get_doc():
    """The active document (never None while Navisworks runs; may be empty)."""
    return Application.ActiveDocument


def get_main_doc():
    """The main document of the main window."""
    return Application.MainDocument


def get_gui():
    """The GuiApplication (main window handle etc.)."""
    return Application.Gui

def get_api_version():
    """Version string of the loaded Navisworks API assembly, e.g. '23.0.x.x'."""
    return clr.GetClrType(Application).Assembly.GetName().Version.ToString()
