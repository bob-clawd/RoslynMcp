using ProjectCore;

namespace ProjectImpl;

public static class WorkItemExtensions
{
    public static string ToDisplay(this WorkItem item)
    {
        return item.ToDisplay(includePriority: false);
    }

    public static string ToDisplay(this WorkItem item, bool includePriority)
    {
        if (!includePriority)
        {
            return $"{item.Id:N}:{item.Name}";
        }

        return $"{item.Id:N}:{item.Name}:P{item.Priority}";
    }
}
