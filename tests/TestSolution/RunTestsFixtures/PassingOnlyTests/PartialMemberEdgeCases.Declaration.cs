namespace PassingOnlyTests.SearchMemberFixtures;

public partial class PartialMemberEdgeCases
{
    public void Run()
    {
        Notify("ready");
    }

    partial void Notify(string message);
}
