/// <summary>Attiva DialogueHUD.ShowDialogue/ClearDialogue, la fonte con priorità più alta tra le tre.</summary>
public class DialogTrigger : MessageTriggerBase {
  protected override string LogTag => "DialogTrigger";

  protected override void Show(string text, float timedDuration) => DialogueHUD.Instance.ShowDialogue(text, timedDuration);

  protected override void Clear() => DialogueHUD.Instance.ClearDialogue();
}
