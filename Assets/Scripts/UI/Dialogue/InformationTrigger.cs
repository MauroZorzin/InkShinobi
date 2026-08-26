/// <summary>
/// Fires DialogueHUD.ShowInformation/ClearInformation — the lowest-priority of its three content
/// sources (Dialogue and the interaction prompt both take precedence over it). See
/// MessageTriggerBase for the shared trigger-volume/dismissal/component-toggle behavior.
/// </summary>
public class InformationTrigger : MessageTriggerBase {
  protected override string LogTag => "InformationTrigger";

  protected override void Show(string text, float timedDuration) => DialogueHUD.Instance.ShowInformation(text, timedDuration);

  protected override void Clear() => DialogueHUD.Instance.ClearInformation();
}
