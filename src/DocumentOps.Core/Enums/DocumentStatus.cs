namespace DocumentOps.Core.Enums;

public enum DocumentStatus
{
    Uploaded = 0,
    Processing = 1,
    Extracted = 2,
    RequiresReview = 3,
    Approved = 4,
    Rejected = 5,
    Exported = 6
}

public enum DiscrepancySeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}
