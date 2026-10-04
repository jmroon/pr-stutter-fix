using System;
using PRStutter.TimingExperiment;

internal static class ObserverChecks
{
    public static void Run()
    {
        var channel = new ObservationChannel<int>();
        channel.Publish(1); // Absent diagnostics must be valid.
        int accepted = 0, dropped = 0, good = 0;
        using var bounded = channel.Subscribe(_ => { if (accepted < 2) accepted++; else dropped++; });
        using var broken = channel.Subscribe(_ => throw new Exception("disk/observer failure"));
        using var healthy = channel.Subscribe(_ => good++);
        for (int i = 0; i < 20000; i++) channel.Publish(i);
        if (accepted != 2 || dropped != 19998 || good != 20000 || channel.Faults != 1)
            throw new Exception("Observer failure or capacity interfered with publication");
        using var replacement = channel.Subscribe(_ => good++);
        broken.Dispose(); // Stale handle must not detach the replacement in its slot.
        channel.Publish(0);
        if (good != 20002) throw new Exception("Stale observer disposal detached replacement");
        Console.WriteLine("PASS: absent, full, failing and replaced diagnostic observers cannot interrupt movement publication or healthy peers.");
    }
}
