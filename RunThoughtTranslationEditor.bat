@echo off
setlocal

if exist "C:\msys64\mingw64\bin\python.exe" (
  "C:\msys64\mingw64\bin\python.exe" "%~dp0ThoughtTranslationEditor.py"
  exit /b %errorlevel%
)

if exist "C:\Users\human\AppData\Local\Programs\Python\Python38\python.exe" (
  "C:\Users\human\AppData\Local\Programs\Python\Python38\python.exe" "%~dp0ThoughtTranslationEditor.py"
  exit /b %errorlevel%
)

python "%~dp0ThoughtTranslationEditor.py"
