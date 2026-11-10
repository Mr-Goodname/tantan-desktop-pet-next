using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace BonbonPet.Services;

/// <summary>Owns only this program's per-user Windows logon registration.</summary>
public static class StartupService
{
    public const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "TantanDesktopPet";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
            return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                is string command && !string.IsNullOrWhiteSpace(command);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            string command = CreateStartupCommand(CurrentExecutablePath());
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
                ?? throw new IOException("无法保存开机自启设置。");
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Keep an existing registration pointed at the executable just launched.</summary>
    public static void RefreshRegisteredPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
        if (key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            is not string registered || string.IsNullOrWhiteSpace(registered)) return;
        string current = CreateStartupCommand(CurrentExecutablePath());
        if (!string.Equals(registered, current, StringComparison.Ordinal))
            key.SetValue(ValueName, current, RegistryValueKind.String);
    }

    // Pure command construction is testable without touching the registry.
    public static string CreateStartupCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Contains('"') ||
            executablePath.Contains('\r') || executablePath.Contains('\n'))
            throw new ArgumentException("程序路径无法用于开机自启。", nameof(executablePath));
        string command = "\"" + Path.GetFullPath(executablePath) + "\" --startup";
        if (command.Length > 260)
            throw new ArgumentException("程序路径过长，请将程序放到较短路径后重新开启开机自启。", nameof(executablePath));
        return command;
    }

    private static string CurrentExecutablePath()
    {
        string? path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            using var process = Process.GetCurrentProcess();
            path = process.MainModule?.FileName;
        }
        return string.IsNullOrWhiteSpace(path)
            ? throw new IOException("无法确定当前程序的位置。") : Path.GetFullPath(path);
    }
}
