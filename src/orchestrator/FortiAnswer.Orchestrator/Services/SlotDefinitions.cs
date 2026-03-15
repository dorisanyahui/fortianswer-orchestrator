using FortiAnswer.Orchestrator.Models;

namespace FortiAnswer.Orchestrator.Services;

/// <summary>
/// Static registry of required slots per issueType.
/// Add or adjust slots here without touching any other class.
/// IssueTypes not present in this map skip slot filling and auto-escalate directly (existing behavior).
/// </summary>
public static class SlotDefinitions
{
    private static readonly Dictionary<string, List<SlotDefinition>> ByIssueType =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["Phishing"] = new()
        {
            new("senderEmail",    "What is the sender's email address?",                       "e.g. attacker@evil.com"),
            new("clickedLink",    "Did you click any link in the email? (yes / no)",           "yes or no"),
            new("affectedDevice", "What device did you receive this on?",                      "e.g. MacBook, Windows laptop"),
            new("accountAccess",  "Can you still log in to your account normally? (yes / no)", "yes or no"),
        },

        ["SuspiciousLogin"] = new()
        {
            new("affectedAccount", "Which account was affected?",                              "e.g. your email address"),
            new("detectedTime",    "When did you first notice this? (approximate time/date)",  "e.g. 2 pm today"),
            new("loginLocation",   "Where was the suspicious login from? (if shown)",          "e.g. China, unknown location"),
            new("mfaApproved",     "Was there an MFA prompt — and was it approved?",           "yes / no / didn't receive one"),
        },

        ["VPN"] = new()
        {
            new("vpnClient",       "Which VPN client are you using?",                          "e.g. GlobalProtect, Cisco AnyConnect"),
            new("errorMessage",    "What error message or code are you seeing?",               "e.g. Authentication failed, Error 619"),
            new("operatingSystem", "What is your operating system?",                           "e.g. Windows 11, macOS 14"),
            new("lastWorked",      "When did VPN last work successfully?",                     "e.g. yesterday, last week"),
        },

        ["MFA"] = new()
        {
            new("mfaMethod",   "Which MFA method are you using?",                              "e.g. Authenticator app, SMS, hardware token"),
            new("deviceType",  "What device are you trying to authenticate on?",               "e.g. iPhone, Android, laptop"),
            new("errorDetail", "What happens when you try? Describe the error.",               "e.g. code expired, no push received"),
            new("lastWorked",  "When did MFA last work for you?",                              "e.g. this morning, 2 days ago"),
        },

        ["EndpointAlert"] = new()
        {
            new("alertType",    "What type of alert did you receive?",                         "e.g. malware detected, suspicious process"),
            new("deviceName",   "What is the hostname or device name?",                        "e.g. LAPTOP-JOHN01"),
            new("affectedUser", "Is the affected user you, or someone else?",                  "me / name of affected user"),
            new("actionsTaken", "Have you taken any action already?",                          "e.g. isolated device, ran scan, nothing yet"),
        },

        ["AccountLockout"] = new()
        {
            new("affectedAccount", "Which account is locked?",                                 "e.g. your email or username"),
            new("triggeredBy",     "What were you doing when it locked?",                      "e.g. logging into VPN, email client"),
            new("multipleDevices", "Are you seeing this on multiple devices? (yes / no)",      "yes or no"),
        },

        ["PasswordReset"] = new()
        {
            new("affectedAccount", "Which account needs a password reset?",                    "e.g. your email address"),
            new("methodTried",     "What reset method have you tried?",                        "e.g. self-service portal, email link"),
            new("errorDetail",     "What issue occurred during the reset attempt?",            "e.g. link expired, portal not loading"),
        },
    };

    /// <summary>Returns true if slot filling is defined for this issueType.</summary>
    public static bool HasSlots(string? issueType) =>
        issueType is not null && ByIssueType.ContainsKey(issueType);

    /// <summary>Returns the ordered list of slots for a given issueType, or empty list.</summary>
    public static List<SlotDefinition> GetSlots(string issueType) =>
        ByIssueType.TryGetValue(issueType, out var slots) ? slots : new List<SlotDefinition>();

    /// <summary>Total number of slots for a given issueType (0 if none defined).</summary>
    public static int GetTotalSlots(string issueType) => GetSlots(issueType).Count;
}
