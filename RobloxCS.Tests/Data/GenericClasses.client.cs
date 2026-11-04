using RobloxCS.Types.Attributes;

namespace RobloxCS.Tests.Data;

internal class GenericBox<T> {
    private T Value;

    public GenericBox(T value) {
        Value = value;
    }

    public virtual T Get() => Value;
    public void Set(T value) => Value = value;
}

#pragma warning disable CS8618, CS0649
internal class GenericDefaults<T> {
    private T Value;
    public T Get() => Value;
    public T Default() => default!;
}
#pragma warning restore CS8618, CS0649

internal class GenericEscaped<@end, @string> {
    private @end First;
    private @string Second;
    public GenericEscaped(@end @local, @string @type) {
        First = @local;
        Second = @type;
    }
    public @end GetFirst() => First;
    public @string GetSecond() => Second;
}

internal static class GenericClasses {
    [EntryPoint]
    public static void Run() {
        var number = new GenericBox<int>(12);
        Console.WriteLine(number.Get());
        number.Set(25);
        Console.WriteLine(number.Get());
        GenericBox<string> text = new GenericBox<string>("hello");
        Console.WriteLine(text.Get());
        text.Set("world");
        Console.WriteLine(text.Get());
        var nested = new GenericBox<GenericBox<int>>(number);
        Console.WriteLine(nested.Get().Get());
        GenericBox<string>? optional = new GenericBox<string>("optional");
        if (optional != null) {
            Console.WriteLine(optional.Get());
        }
        Console.WriteLine(new GenericDefaults<int>().Get());
        Console.WriteLine(new GenericDefaults<bool>().Default());
        Console.WriteLine(new GenericDefaults<string>().Get() == null);
        var escaped = new GenericEscaped<int, string>(6, "escaped");
        Console.WriteLine(escaped.GetFirst());
        Console.WriteLine(escaped.GetSecond());
    }
}