using System.Runtime.CompilerServices;

// The in-game test mod (InGameTests\, never published) checks internal pieces such as the appearance
// editor's option queries directly, instead of reaching them through reflection that would silently
// break when a method is renamed. Internal members stay invisible to every other assembly.
[assembly: InternalsVisibleTo("PawnEditor.InGameTests")]
