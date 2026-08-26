/// <summary>
/// Fires DialogueHUD.ShowDialogue/ClearDialogue — the highest-priority of the three content sources,
/// so a dialogue line wins the shared label over both the interaction prompt and any Information
/// hint. See MessageTriggerBase for the shared trigger-volume/dismissal/component-toggle behavior.
/// No speaker/portrait fields here: there is only one dialogue "speaker" in this game (the player),
/// so DialogueHUD shows a fixed portrait for every dialogue line rather than something this trigger
/// would need to configure per instance.
/// </summary>
public class DialogTrigger : MessageTriggerBase {
  protected override string LogTag => "DialogTrigger";

  protected override void Show(string text, float timedDuration) => DialogueHUD.Instance.ShowDialogue(text, timedDuration);

  protected override void Clear() => DialogueHUD.Instance.ClearDialogue();
}
