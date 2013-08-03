UniThread
=========

Using thread in Unity can be problematic because if you join on the thread, it blocks the main thread. Many Unity classes can't be touched while off the main thread. UniThread allows you to spin up threads that execute callbacks on the main thread once they are finished.
