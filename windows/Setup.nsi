Unicode true
ManifestSupportedOS all
ManifestDPIAware true
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\YTPlus"
InstallDirRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\YTPlus" "InstallLocation"
Name "YT+"
OutFile "${SETUP_OUTPUT}"
SetCompressor /SOLID lzma
SetCompressorDictSize 32
CRCCheck force

!include "MUI2.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!include "Sections.nsh"

!define MUI_ICON "YTPlus.ico"
!define MUI_UNICON "YTPlus.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEFINISHPAGE_BITMAP "wizard.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "wizard.bmp"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP "header.bmp"
!define MUI_HEADERIMAGE_UNBITMAP "header.bmp"

!define MUI_WELCOMEPAGE_TITLE "$(WELCOME_TITLE)"
!define MUI_WELCOMEPAGE_TEXT "$(WELCOME_TEXT)"
!define MUI_DIRECTORYPAGE_TEXT_TOP "$(DIR_TOP)"
!define MUI_COMPONENTSPAGE_SMALLDESC
!define MUI_FINISHPAGE_TITLE "$(FINISH_TITLE)"
!define MUI_FINISHPAGE_TEXT "$(FINISH_TEXT)"
!define MUI_FINISHPAGE_RUN "$INSTDIR\YTPlusApp\YTPlus.exe"
!define MUI_FINISHPAGE_RUN_TEXT "$(FINISH_RUN)"

!insertmacro MUI_PAGE_WELCOME
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE ValidateInstallDir
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Russian"

LangString WELCOME_TITLE ${LANG_RUSSIAN} "Добро пожаловать в установку YT+"
LangString WELCOME_TITLE ${LANG_ENGLISH} "Welcome to YT+ Setup"

LangString WELCOME_TEXT ${LANG_RUSSIAN} "YT+ скачивает видео и звук с YouTube, TikTok и Pinterest, а также оригинальные фото с Pinterest.$\r$\n$\r$\nПриложение устанавливается для текущего пользователя и уже включает yt-dlp, FFmpeg и Node.js — дополнительно ничего скачивать не нужно.$\r$\n$\r$\nНажмите «Далее», чтобы продолжить."
LangString WELCOME_TEXT ${LANG_ENGLISH} "YT+ downloads video and audio from YouTube, TikTok, and Pinterest, as well as original photos from Pinterest.$\r$\n$\r$\nThe app installs for the current user and already bundles yt-dlp, FFmpeg, and Node.js — no extra downloads required.$\r$\n$\r$\nClick Next to continue."

LangString DIR_TOP ${LANG_RUSSIAN} "Выберите папку, в которую нужно установить YT+. Чтобы использовать другой каталог, нажмите «Обзор…»."
LangString DIR_TOP ${LANG_ENGLISH} "Choose the folder in which to install YT+. Click Browse to select a different folder."

LangString FINISH_TITLE ${LANG_RUSSIAN} "YT+ успешно установлен"
LangString FINISH_TITLE ${LANG_ENGLISH} "YT+ Setup Complete"

LangString FINISH_TEXT ${LANG_RUSSIAN} "Установка завершена. Вы можете запустить YT+ прямо сейчас или позже из меню «Пуск»."
LangString FINISH_TEXT ${LANG_ENGLISH} "Installation is complete. You can launch YT+ now or open it later from the Start Menu."

LangString FINISH_RUN ${LANG_RUSSIAN} "Запустить YT+"
LangString FINISH_RUN ${LANG_ENGLISH} "Launch YT+"

LangString ERR_RUNNING ${LANG_RUSSIAN} "Закройте YT+ и запустите установщик снова."
LangString ERR_RUNNING ${LANG_ENGLISH} "Please close YT+ and run the installer again."

LangString ERR_WINVER ${LANG_RUSSIAN} "Для этой сборки нужна 64-битная Windows 10 или 11."
LangString ERR_WINVER ${LANG_ENGLISH} "This build requires 64-bit Windows 10 or 11."

LangString ERR_DIR_WRITE ${LANG_RUSSIAN} "Нет прав для записи в выбранную папку:$\r$\n$INSTDIR$\r$\n$\r$\nВыберите другую папку (например, в профиле пользователя или на другом диске)."
LangString ERR_DIR_WRITE ${LANG_ENGLISH} "Cannot write to the selected folder:$\r$\n$INSTDIR$\r$\n$\r$\nPlease choose another folder (for example, inside your user profile or on another drive)."

LangString ERR_INSTALL ${LANG_RUSSIAN} "Не удалось установить YT+."
LangString ERR_INSTALL ${LANG_ENGLISH} "Failed to install YT+."

LangString SEC_APP_NAME ${LANG_RUSSIAN} "YT+ (приложение и зависимости)"
LangString SEC_APP_NAME ${LANG_ENGLISH} "YT+ (app & bundled tools)"

LangString SEC_DESKTOP_NAME ${LANG_RUSSIAN} "Ярлык на рабочем столе"
LangString SEC_DESKTOP_NAME ${LANG_ENGLISH} "Desktop shortcut"

LangString DESC_APP ${LANG_RUSSIAN} "Основное приложение YT+, встроенные yt-dlp, FFmpeg, Node.js и ярлык в меню «Пуск»."
LangString DESC_APP ${LANG_ENGLISH} "Main YT+ application, bundled yt-dlp, FFmpeg, Node.js, and Start Menu shortcut."

LangString DESC_DESKTOP ${LANG_RUSSIAN} "Создать ярлык YT+ на рабочем столе для быстрого запуска."
LangString DESC_DESKTOP ${LANG_ENGLISH} "Create a YT+ shortcut on your Desktop for quick access."

VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=${LANG_RUSSIAN} "ProductName" "YT+"
VIAddVersionKey /LANG=${LANG_RUSSIAN} "CompanyName" "YT+"
VIAddVersionKey /LANG=${LANG_RUSSIAN} "FileDescription" "Установщик YT+ (YouTube, TikTok, Pinterest)"
VIAddVersionKey /LANG=${LANG_RUSSIAN} "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=${LANG_RUSSIAN} "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=${LANG_RUSSIAN} "LegalCopyright" "YT+"
VIAddVersionKey /LANG=${LANG_ENGLISH} "ProductName" "YT+"
VIAddVersionKey /LANG=${LANG_ENGLISH} "CompanyName" "YT+"
VIAddVersionKey /LANG=${LANG_ENGLISH} "FileDescription" "YT+ Setup (YouTube, TikTok, Pinterest)"
VIAddVersionKey /LANG=${LANG_ENGLISH} "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=${LANG_ENGLISH} "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=${LANG_ENGLISH} "LegalCopyright" "YT+"

!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\YTPlus"

!macro RequireClosedApp
    System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\YTPlus.Desktop") p .r0'
    ${If} $0 P<> 0
        System::Call 'kernel32::CloseHandle(p r0)'
        MessageBox MB_OK|MB_ICONEXCLAMATION "$(ERR_RUNNING)" /SD IDOK
        SetErrorLevel 1
        Abort
    ${EndIf}
!macroend

!macro RenameWithRetry SRC DST
    StrCpy $1 0
    ${Do}
        ClearErrors
        Rename "${SRC}" "${DST}"
        ${IfNot} ${Errors}
            ${Break}
        ${EndIf}
        IntOp $1 $1 + 1
        ${If} $1 >= 15
            SetErrors
            ${Break}
        ${EndIf}
        Sleep 300
    ${Loop}
!macroend

Function .onInit
    SetShellVarContext current
    ${IfNot} ${RunningX64}
    ${OrIfNot} ${AtLeastWin10}
        MessageBox MB_OK|MB_ICONSTOP "$(ERR_WINVER)" /SD IDOK
        SetErrorLevel 1
        Abort
    ${EndIf}
    !insertmacro RequireClosedApp
FunctionEnd

Function ValidateInstallDir
    StrCpy $2 "0"
    IfFileExists "$INSTDIR\*.*" check_write 0
    StrCpy $2 "1"
    ClearErrors
    CreateDirectory "$INSTDIR"
    IfErrors not_writable

check_write:
    ClearErrors
    FileOpen $0 "$INSTDIR\.ytplus-write-test.tmp" w
    IfErrors cleanup_unwritable
    FileClose $0
    Delete "$INSTDIR\.ytplus-write-test.tmp"
    ${If} $2 == "1"
        RMDir "$INSTDIR"
    ${EndIf}
    Return

cleanup_unwritable:
    ${If} $2 == "1"
        RMDir "$INSTDIR"
    ${EndIf}
not_writable:
    MessageBox MB_OK|MB_ICONEXCLAMATION "$(ERR_DIR_WRITE)" /SD IDOK
    Abort
FunctionEnd

Function un.onInit
    SetShellVarContext current
    !insertmacro RequireClosedApp
FunctionEnd

Section "$(SEC_APP_NAME)" SEC_APP
    SectionIn RO

    IfFileExists "$INSTDIR\YTPlusApp\YTPlus.exe" start_stage 0
    IfFileExists "$INSTDIR\YTPlusApp.backup\YTPlus.exe" 0 start_stage
    RMDir /r "$INSTDIR\YTPlusApp"
    !insertmacro RenameWithRetry "$INSTDIR\YTPlusApp.backup" "$INSTDIR\YTPlusApp"

start_stage:
    RMDir /r "$INSTDIR\YTPlusApp.stage"
    SetOutPath "$INSTDIR\YTPlusApp.stage"
    ClearErrors
    File /r "${APP_SOURCE}/*"
    IfErrors install_error
    SetOutPath "$INSTDIR"

    IfFileExists "$INSTDIR\YTPlusApp\YTPlus.exe" backup_old clean_broken

backup_old:
    RMDir /r "$INSTDIR\YTPlusApp.backup"
    !insertmacro RenameWithRetry "$INSTDIR\YTPlusApp" "$INSTDIR\YTPlusApp.backup"
    IfErrors install_error
    Goto activate

clean_broken:
    RMDir /r "$INSTDIR\YTPlusApp"

activate:
    !insertmacro RenameWithRetry "$INSTDIR\YTPlusApp.stage" "$INSTDIR\YTPlusApp"
    IfErrors restore_old
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    IfErrors uninstall_error
    RMDir /r "$INSTDIR\YTPlusApp.backup"

    CreateDirectory "$SMPROGRAMS\YT+"
    SetOutPath "$INSTDIR\YTPlusApp"
    CreateShortCut "$SMPROGRAMS\YT+\YT+.lnk" "$INSTDIR\YTPlusApp\YTPlus.exe"
    SetOutPath "$INSTDIR"

    SectionGetSize ${SEC_APP} $2
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "YT+"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "YT+"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\YTPlusApp\YTPlus.exe"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" $2
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
    Goto finished

uninstall_error:
    !insertmacro RenameWithRetry "$INSTDIR\YTPlusApp" "$INSTDIR\YTPlusApp.stage"
restore_old:
    IfFileExists "$INSTDIR\YTPlusApp.backup\YTPlus.exe" 0 install_error
    RMDir /r "$INSTDIR\YTPlusApp"
    !insertmacro RenameWithRetry "$INSTDIR\YTPlusApp.backup" "$INSTDIR\YTPlusApp"
install_error:
    SetOutPath "$INSTDIR"
    RMDir /r "$INSTDIR\YTPlusApp.stage"
    SetErrorLevel 1
    Abort "$(ERR_INSTALL)"
finished:
SectionEnd

Section "$(SEC_DESKTOP_NAME)" SEC_DESKTOP
    SetOutPath "$INSTDIR\YTPlusApp"
    CreateShortCut "$DESKTOP\YT+.lnk" "$INSTDIR\YTPlusApp\YTPlus.exe"
    SetOutPath "$INSTDIR"
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
    !insertmacro MUI_DESCRIPTION_TEXT ${SEC_APP} "$(DESC_APP)"
    !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DESKTOP} "$(DESC_DESKTOP)"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
    Delete "$DESKTOP\YT+.lnk"
    Delete "$SMPROGRAMS\YT+\YT+.lnk"
    RMDir "$SMPROGRAMS\YT+"
    DeleteRegKey HKCU "${UNINSTALL_KEY}"
    RMDir /r "$INSTDIR\YTPlusApp"
    RMDir /r "$INSTDIR\YTPlusApp.stage"
    RMDir /r "$INSTDIR\YTPlusApp.backup"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"
SectionEnd
