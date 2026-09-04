/// <summary>Attiva DialogueHUD.ShowInformation/ClearInformation, la fonte con priorità più bassa tra le tre.</summary>
public class InformationTrigger : MessageTriggerBase {
  protected override string LogTag => "InformationTrigger";

  protected override void Show(string text, float timedDuration) => DialogueHUD.Instance.ShowInformation(text, timedDuration);

  protected override void Clear() => DialogueHUD.Instance.ClearInformation();
}
