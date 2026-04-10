using CommunityToolkit.Mvvm.ComponentModel;

namespace LibreCode.Features.AI;

/// <summary>Available AI interaction modes.</summary>
public enum AiMode
{
    Agent,
    Plan,
    Debug,
    Ask
}

/// <summary>Type of message in the unified AI timeline.</summary>
public enum AiMessageType
{
    User,
    Assistant,
    AgentStep
}

/// <summary>
/// A single entry in the unified AI conversation timeline.
/// Derives from ObservableObject so the UI updates when Content changes during streaming.
/// </summary>
public sealed partial class AiMessage : ObservableObject
{
    [ObservableProperty] private AiMessageType _type;
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private string? _title;
    [ObservableProperty] private Agent.AgentStepStatus _stepStatus;
}
