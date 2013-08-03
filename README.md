UniThread
=========

Using thread in Unity can be problematic because if you join on the thread, it blocks the main thread. Many Unity classes can't be touched while off the main thread. UniThread allows you to spin up threads that execute callbacks on the main thread once they are finished.

Installation
============
Copy the UniThread.cs and UniThread.meta (optional) to your <Unity Project>/Assets/Plugins folder.

Usage
=====
How UniThread works is that it wraps a method/lambda into a thread (this is your "job"), and then waits for the job to complete. Once the thread is finished, it calls the callback you provided depending on success or failure (onComplete is called on success, onError is called on Failure). Your job can provide data back to the main thread by returning any single object (although this could be a class or collection that holds other things). When the onComplete callback is fired, it is passed this object. You'll need to cast it to use it properly.

```csharp
UniThread.AddThread(
    gameObject.name, // name the job after my game object
	() => {
		var result = BuildLargeMesh();
		return result; // if your task doesn't have anything to return, just return null
	},
	(result) => GetComponent<MeshFilter>().mesh = (result as MeshResults).GetUnityMesh(), // executes on the main thread, can touch Unity
	Debug.LogException // Debug.LogException is a good default to use if you don't have anything to handle the error
);
```

TODO:
- [ ] Document grouped thread usage
- [ ] Log levels and debugging options
- [ ] Published on the Asset Store
