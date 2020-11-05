namespace MetadataExplorer
{
    public class Property
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Class { get; set; }
    }

    public class NavProperty : Property
    {
        public bool ContainsTarget { get; set; }
        public bool IsCollection { get; set; }
    }

    public abstract class GraphType
    {
        public string ItemType { get; set; }
        public string Name { get; set; }
        public string FullName { get; set; }
        public abstract string CssClass { get; }
    }

    public class EntityType : GraphType
    {
        public bool IsAbstract { get; set; }
        public string BaseType { get; set; }
        public Property[] Properties { get; set; }
        public EntityType()
        {
            ItemType = nameof(EntityType);
        }
        public override string CssClass { get { return IsAbstract ? "bg-info" : "bg-primary"; } }
    }

    public class ComplexType : GraphType
    {
        public string BaseType { get; set; }
        public Property[] Properties { get; set; }
        public ComplexType()
        {
            ItemType = nameof(ComplexType);
        }
        public override string CssClass { get { return "bg-success"; } }
    }

    public class EnumType : GraphType
    {
        public string[] Members { get; set; }
        public EnumType()
        {
            ItemType = nameof(EnumType);
        }
        public override string CssClass { get { return "bg-dark"; } }
    }

    public class Singleton : GraphType
    {
        public Singleton()
        {
            ItemType = nameof(Singleton);
        }
        public override string CssClass { get { return "bg-danger"; } }
    }

    public class EntitySet : GraphType
    {
        public EntitySet()
        {
            ItemType = nameof(EntitySet);
        }
        public override string CssClass { get { return "bg-danger"; } }
    }
}