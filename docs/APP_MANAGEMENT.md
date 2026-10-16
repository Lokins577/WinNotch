# Application and package management

Enable **Application management** in the feature catalog, then press
`Ctrl+Alt+G`.

## Review-first workflow

Opening or refreshing the window is read-only:

- WinGet `upgrade` is run without `--all` to preview available upgrades;
- installed applications are read from current-user and 32/64-bit machine
  uninstall registrations;
- cleanup scans configured roots and returns individual old files;
- TCPv4/TCPv6 and UDPv4/UDPv6 owner tables are read through the extended IP
  Helper tables; and
- process names are resolved only for display and policy checks.

Each mutation has a separate review:

- a selected upgrade launches WinGet for one exact validated package ID;
- installer handoff accepts only EXE/MSI/MSIX/MSIXBundle/AppInstaller files;
- uninstall launches the selected application's registered interactive command;
- cleanup moves only selected, revalidated files to the Recycle Bin; and
- force termination requires typing the exact process name.

The default process action is `CloseMainWindow`, which leaves save prompts and
cancellation under application control. WinNotch refuses to target itself and
an explicit allow-deny policy protects core Windows shell/security processes.
Force termination never expands to a process tree.

## Cleanup and message-download retention

Default candidates are old files in the user temporary directory and common
Edge/Chrome caches. Telegram Desktop and WhatsApp download folders are reviewed
with the messaging-retention age. Additional messaging folders must be selected
explicitly. Scans skip reparse-point directories, cap results at 5,000 files,
never select directories, and never delete automatically.

- [WinGet upgrade](https://learn.microsoft.com/windows/package-manager/winget/upgrade)
- [`GetExtendedTcpTable`](https://learn.microsoft.com/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable)
- [Recycle Bin deletion](https://learn.microsoft.com/dotnet/api/microsoft.visualbasic.fileio.filesystem.deletefile)
