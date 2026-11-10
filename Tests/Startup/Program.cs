using BonbonPet.Services;

int checks = 0;
void Check(bool pass, string message)
{
    if (!pass) throw new InvalidOperationException(message);
    checks++;
}
string unicodePath = @"C:\Program Files\弹弹桌宠\弹弹桌宠.exe";
Check(StartupService.CreateStartupCommand(unicodePath) == "\"" + unicodePath + "\" --startup",
    "A Windows logon command quotes spaces and Unicode, with the silent startup argument outside the path");
string relativePath = "弹弹桌宠.exe";
Check(StartupService.CreateStartupCommand(relativePath) == "\"" + Path.GetFullPath(relativePath) + "\" --startup",
    "A relative source path becomes an absolute Windows logon command");
foreach (string invalid in new[] { "", " ", "bad\"path.exe", "bad\rpath.exe", "bad\npath.exe", "C:\\" + new string('x', 270) + ".exe" })
{
    bool rejected = false;
    try { StartupService.CreateStartupCommand(invalid); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "Invalid or overlong Windows logon commands are rejected before registry access");
}
Console.WriteLine($"{checks} startup command checks passed without accessing the registry.");
