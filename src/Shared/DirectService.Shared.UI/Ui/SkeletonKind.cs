namespace DirectService.Shared.UI.Ui;

/// <summary>Screen kinds of the board, used for loading skeletons.</summary>
public enum SkeletonKind
{
    List,
    Form,
    Report,
    Detail,
    Dashboard,
}

/// <summary>The five states every screen has (step 6): default (content), loading, empty, error; success is a toast or next step.</summary>
public enum LoadState
{
    Loading,
    Ready,
    Empty,
    Error,
}
