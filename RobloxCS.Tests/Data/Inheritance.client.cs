using RobloxCS.Types.Attributes;

namespace RobloxCS.Tests.Data;

internal class InheritanceChild : InheritanceBase {
    private int Extra = 7;

    public InheritanceChild(int value) : base(value + 1) {
        Console.WriteLine("child");
    }

    public override int Compute() => base.Compute() + Extra;
    public override int Amount {
        get => base.Amount + Extra;
        set => base.Amount = value - Extra;
    }
}

internal class InheritanceBase {
    protected int Value;

    public InheritanceBase(int value) {
        Value = value;
        Console.WriteLine(Compute());
    }

    public virtual int Compute() => Value;
    public virtual int Amount {
        get => Value;
        set => Value = value;
    }
}

internal class InheritanceDefaultBase {
    protected int Start = 3;
    public int Read() => Start;
}

internal class InheritanceImplicitChild : InheritanceDefaultBase {
    private int End = 4;
    public int Sum() => Start + End;
}

internal class InheritanceOptionalBase {
    private int Value;
    public InheritanceOptionalBase(int value = 5) => Value = value;
    public int Read() => Value;
}

internal class InheritanceOptionalChild : InheritanceOptionalBase { }

internal abstract class InheritanceAbstract {
    public abstract int Result();
}

internal class InheritanceConcrete : InheritanceAbstract {
    public override int Result() => 42;
}

internal class InheritanceGenericBase<T> {
    private T Value;
    public InheritanceGenericBase(T value) => Value = value;
    public virtual T Read() => Value;
}

internal class InheritanceGenericChild<T> : InheritanceGenericBase<T> {
    public InheritanceGenericChild(T value) : base(value) { }
    public override T Read() => base.Read();
}

internal static class Inheritance {
    [EntryPoint]
    public static void Run() {
        InheritanceBase value = new InheritanceChild(10);
        Console.WriteLine(value.Compute());
        Console.WriteLine(value.Amount);
        value.Amount = 30;
        Console.WriteLine(value.Compute());
        var implicitChild = new InheritanceImplicitChild();
        Console.WriteLine(implicitChild.Read());
        Console.WriteLine(implicitChild.Sum());
        InheritanceAbstract concrete = new InheritanceConcrete();
        Console.WriteLine(concrete.Result());
        Console.WriteLine(new InheritanceOptionalChild().Read());
        InheritanceGenericBase<string> generic = new InheritanceGenericChild<string>("generic");
        Console.WriteLine(generic.Read());
    }
}