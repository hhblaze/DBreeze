@setlocal enableextensions enabledelayedexpansion
@echo off
set v="%~dp0"
set v=!variable:~0,-2!
"%~dp0xamarin-component.exe" package !v!