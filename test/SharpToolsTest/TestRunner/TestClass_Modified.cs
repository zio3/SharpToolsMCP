namespace TestProject;

public class TestClass
{
    public string Name { get; set; } = string.Empty;
    
    public string GetGreeting()
    {
        return $"Hello, {Name}!";
    }
public bool IsEmpty => string.IsNullOrEmpty(Name);}