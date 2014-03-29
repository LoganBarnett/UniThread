// This project is licensed under The MIT License (MIT)
//
// Copyright 2013 Logan Barnett
//
//	Permission is hereby granted, free of charge, to any person obtaining a copy
//		of this software and associated documentation files (the "Software"), to deal
//		in the Software without restriction, including without limitation the rights
//		to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//		copies of the Software, and to permit persons to whom the Software is
//		furnished to do so, subject to the following conditions:
//
//		The above copyright notice and this permission notice shall be included in
//		all copies or substantial portions of the Software.
//
//		THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//		IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//		FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//		AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//		LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//		OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//		THE SOFTWARE.
//
// Please direct questions, patches, and suggestions to the project page at
// https://github.com/LoganBarnett/UniThread

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

public class UniThread : MonoBehaviour {
	const string UNI_THREAD_RUNNER_GAME_OBJECT_NAME = "_UniThread";
	static UniThread threadRunner = null;
	static Dictionary<string, TaskGroup> groups = new Dictionary<string, TaskGroup>();
	static List<TaskRunner> taskRunners = new List<TaskRunner>();
	static ReaderWriterLockSlim groupLock = new ReaderWriterLockSlim();
	static ReaderWriterLockSlim taskLock = new ReaderWriterLockSlim();
	static Dictionary<TaskGroup, ReaderWriterLockSlim> groupSpecificTaskLock = new Dictionary<TaskGroup, ReaderWriterLockSlim>();
//	int framesUsed = 0;
//	int threadsConsumed = 0;
	
	class TaskRunner {
		public string Name {get; set;}
		public string GroupName { get; set; }
		public int PositionInGroup { get; set; }
		public System.Func<object> TaskWithResult { get; set; }
		public System.Action Task { get; set; }
		public bool IsComplete { get; set; }
		public System.Action<object> OnComplete { get; set; }
		public System.Action<System.Exception> OnError { get; set; }
		public object Result { get; set; }
		public object[] GroupResults { get; set; }
		public System.Exception Exception { get; set; }
		public bool ReturnsResult { get; set; }
		public bool IsThreaded { get; set; }
	}

	// TODO: Should make this OO. UniThread itself is doing way too much of the bookkeeping that should be internal
	// to TaskGroup
	class TaskGroup {
		private int NumberOfThreadsComplete { get; set; }
		public string Name { get; set; }
		// TODO: not sure how else to handle this. This creates a problem with dynamic groups though - perhaps should be
		// mutated as the number of threads changes?
		public int TotalThreads { get; set; }
		public bool Async { get; set; }
		public Dictionary<string, object> Results { get; set; }
		public List<TaskRunner> TaskRunners { get; set; }
		public System.Action<Dictionary<string, object>> OnComplete { get; set; }
		public bool AllThreadsComplete { get { return NumberOfThreadsComplete == TotalThreads; } }
		public bool HasFiredExecutor { get; set; }
		
		public static TaskGroup Empty {
			get {
				return new TaskGroup {TaskRunners = new List<TaskRunner>(), Results = new Dictionary<string, object>()};
			}
		}
		
		public void Complete(TaskRunner taskRunner) {
			++NumberOfThreadsComplete;
		}
	}

	public static void LogException(System.Exception exception) {
		Debug.LogError(string.Format("{0} - {1}:\n{2}", exception, exception.Message, exception.StackTrace));
	}
	
	// TODO: Add a mechanism for grouping threads (a groupOnComplete and groupOnError). Great for batched jobs
	// TODO: See about making this generic somehow
	// TODO: These threads don't necessarily get cleaned up on ApplicationQuit (at least in the editor - should ensure).
	/// <summary>
	/// Adds the delegate to the ThreadPool's worker queue as a thread.
	/// Threads constructed in this manner are cleaned up automatically on application exit.
	/// Uses the actor pattern - <c>jobTothread</c> does its work asynchronously, and <c>onComplete</c> gets executed
	/// against the main Unity thread during an update cycle. jobToThread returns a result, which is then passed on to
	/// onComplete. It's safe to use null if you have no meaningful data to pass along.
	/// </summary>
	/// <param name='name'>
	/// The name of the thread - very useful for error handling.
	/// </param>
	/// <param name='jobToThread'>
	/// Delegate/Lambda/Method to thread. Must return a System.Object (anything, including null).
	/// </param>
	/// <param name='onComplete'>
	/// This delegate/lambda/method gets executed on Unity's main thread when the thread completes.
	/// onComplete is passed the object returned from jobToThread.
	/// </param>
	/// <param name='onError'>
	/// This delegate/lambda/method gets executed on Unity's main thread when the jobToThread throws an exception.
	/// It takes a System.Exception - the error thrown from jobToThread.
	/// </param>
	public static void EnqueueAsyncUnityTask(
		string name,
		System.Func<object> jobToThread,
		System.Action<object> onComplete,
		System.Action<System.Exception> onError) {
		
		EnsureUniThreadRunner();
		
		var taskRunner = new TaskRunner {
			Name = name,
			OnComplete = onComplete,
			OnError = onError,
			TaskWithResult = jobToThread,
			ReturnsResult = true,
		};

		Add(taskRunner);
	}

	// TODO: Make generic
	// TODO: Document
	// TODO: Update readme to reflect method name changes
	public static void EnqueueAsyncUnityTask(
		string name,
		string groupName,
		System.Func<object> jobToThread,
		System.Action<object> onComplete,
		System.Action<System.Exception> onError) {
		
		EnsureUniThreadRunner();
		
		if(!groups.ContainsKey(groupName)) {
			throw new System.Exception(string.Format(
				"No group named '{0}' found. Declare with UniThread.CreateGroup",
				groupName
				));
		}
		var threadHandler = new TaskRunner {
			Name = name,
			GroupName = groupName,
			TaskWithResult = jobToThread,
			OnComplete = onComplete,
			OnError = onError,
			ReturnsResult = true,
			IsThreaded = true,
		};
		
		AddToGroup(threadHandler);
	}

	// need an object result so jobs can pass some data back to the group's complete task.
	// TODO: Document
	public static void EnqueueAsyncUnityTask(
		string name,
		string groupName,
		System.Func<object> task,
		System.Action<System.Exception> onError) {

		EnsureUniThreadRunner();

		if(!groups.ContainsKey(groupName)) {
			throw new System.Exception(string.Format(
				"No group named '{0}' found. Declare with UniThread.CreateGroup",
				groupName
				)
           );
		}

		var threadHandler = new TaskRunner {
			Name = name,
			GroupName = groupName,
			OnError = onError,
			TaskWithResult = task,
			ReturnsResult = true,
			IsThreaded = true,
		};

		AddToGroup(threadHandler);
	}

	// TODO: Document
	// Doesn't hit the Unity side when done
	public static void EnqueueAsyncTask(
		string name,
		System.Action taskToRun,
		System.Action<System.Exception> onError) {
		
		EnsureUniThreadRunner();
		
		var threadHandler = new TaskRunner {
			Name = name,
			Task = taskToRun,
			OnError = onError,
			IsThreaded = true,
		};
		
		Add(threadHandler);
	}

	// Sometimes we just want to run a task synchronously
	public static void EnqueueSyncedUnityTask(string name, System.Action task, System.Action<System.Exception> onError) {
		var taskRunner = new TaskRunner {
			Name = name,
			IsThreaded = false,
			Task = task,
			OnError = onError,
		};

		Add(taskRunner);
	}

	// TODO: Document
	public static void CreateGroup(
		string groupName,
		int totalThreads,
		bool @async,
		System.Action<Dictionary<string, object>> onComplete) {


		// TODO: May need to terminate old threads on the old group if this is squashing it
		var taskGroup = new TaskGroup {
			Name = groupName,
			Async = @async,
			TotalThreads = totalThreads,
			OnComplete = onComplete,
			TaskRunners = new List<TaskRunner>(),
			Results = new Dictionary<string, object>(),
		};
		groupSpecificTaskLock.Add(taskGroup, new ReaderWriterLockSlim());

		groupLock.EnterWriteLock();
		try {
			groups.Add(groupName, taskGroup);
		}
		finally {
			groupLock.ExitWriteLock();
		}
	}
	
	static void ThreadExecuter(System.Object state) {
		try {
			if(typeof(TaskGroup) == state.GetType()) RunGroupThread((TaskGroup)state);
			else if(typeof(TaskRunner) == state.GetType()) RunTaskThread((TaskRunner)state);
			else throw new System.Exception(string.Format("Type '{0}' not supported!",  state.GetType()));
		}
		catch(System.Exception exception) {
			Debug.LogError(string.Format(
				"Error in thread executer: {0} - {1}\nSTACK\n{2}\nENDSTACK\n",
				exception,
				exception.Message,
				exception.StackTrace
			));
		}
	}

	static void RunGroupThread(TaskGroup taskGroup) {
		try {
			taskGroup.OnComplete(taskGroup.Results);
		}
		catch(System.Exception exception) {
			// TODO: Actually allow the user to intercept and handle, for now just log it
			Debug.LogException(exception);
		}
		finally {
			groupLock.EnterWriteLock();
			try {
				groups.Remove(taskGroup.Name);
				groupSpecificTaskLock.Remove(taskGroup);
			}
			finally {
				groupLock.ExitWriteLock();
			}
		}
	}

	static void RunTaskThread(TaskRunner taskRunner) {
		try {
			if(taskRunner.ReturnsResult) taskRunner.Result = taskRunner.TaskWithResult();
			else taskRunner.Task();
		}
		catch(System.Exception exception) {
			taskRunner.Exception = exception;
		}
		finally {
			taskRunner.IsComplete = true;
		}
		if(taskRunner.GroupName != null) {
			groupLock.EnterReadLock();
			try {
				var group = groups[taskRunner.GroupName];
				group.Complete(taskRunner);
				if(group.AllThreadsComplete && !group.HasFiredExecutor) {
					groupSpecificTaskLock[group].EnterReadLock();
					try {
						for(var i = 0; i < group.TaskRunners.Count(); ++i) {
							group.Results[group.TaskRunners[i].Name] = group.TaskRunners[i].Result;
						}
						if(group.Async) {
							ThreadPool.QueueUserWorkItem(new WaitCallback(ThreadExecuter), group);
							group.HasFiredExecutor = true;
						}
					}
					finally {
						groupSpecificTaskLock[group].ExitReadLock();
					}
				}
			}
			catch(KeyNotFoundException) {
				Debug.LogError(string.Format("Could not find group '{0}'", taskRunner.GroupName));
			}
			finally {
				groupLock.ExitReadLock();
			}
		}
	}
	
	static void EnsureUniThreadRunner() {
		// we can't compare threadRunner to null when we're already in a thread - so just assume that we got here via
		// UniThread and therefore can assume the reference is valid
		// However, Unity's Mono shows the current thread as always being the same as the main thread.
		// However however however, UniThread kicks off threads in a threadpool, and Unity's main thread is not pooled.
		if(Thread.CurrentThread.IsThreadPoolThread) return;
		if(threadRunner != null) return;
	
		var threadRunnerGameObject = GameObject.Find(UNI_THREAD_RUNNER_GAME_OBJECT_NAME);
		if(threadRunnerGameObject == null) {
			threadRunnerGameObject = new GameObject(UNI_THREAD_RUNNER_GAME_OBJECT_NAME, typeof(UniThread));
		}
		threadRunner = threadRunnerGameObject.GetComponent<UniThread>();
		if(threadRunner == null) threadRunner = threadRunnerGameObject.AddComponent<UniThread>();
	}
	
	static void Add(TaskRunner taskRunner) {
		taskLock.EnterWriteLock();
		try {
			taskRunners.Add(taskRunner);
		}
		finally {
			taskLock.ExitWriteLock();
		}
		if(taskRunner.IsThreaded) ThreadPool.QueueUserWorkItem(new WaitCallback(ThreadExecuter), taskRunner);
	}

	static void AddToGroup(TaskRunner taskRunner) {
		groupLock.EnterReadLock();
		TaskGroup group = null;
		try {
			group = groups[taskRunner.GroupName];
		}
		finally {
			groupLock.ExitReadLock();
		}

		groupSpecificTaskLock[group].EnterWriteLock();
		try {

			var position = group.TaskRunners.Count;
			taskRunner.PositionInGroup = position;

			group.TaskRunners.Add(taskRunner);
			ThreadPool.QueueUserWorkItem(new WaitCallback(ThreadExecuter), taskRunner);
		}
		finally {
			groupSpecificTaskLock[group].ExitWriteLock();
		}

	}
	
	void Awake() {
		DontDestroyOnLoad(gameObject);
	}
	
	void Update() {

		taskLock.EnterReadLock();
		var handlersToRemove = new bool[taskRunners.Count];
		try {
			for(var i = 0; i < taskRunners.Count; ++i) {

				var taskRunner = taskRunners[i];

				if(!taskRunner.IsComplete && taskRunner.IsThreaded) continue;
				if(!taskRunner.IsThreaded) {
					try {
						if(taskRunner.Task != null) taskRunner.Task();
						else taskRunner.Result = taskRunner.TaskWithResult();
					}
					catch(System.Exception exception) {
						taskRunner.Exception = exception;
						if(taskRunner.OnError != null) taskRunner.OnError(taskRunner.Exception);
					}
					taskRunner.IsComplete = true;
					handlersToRemove[i] = true;
					continue;
				}
				if(taskRunner.TaskWithResult == null) {
					handlersToRemove[i] = true;
					continue;
				}

				try {
					if(taskRunner.Exception == null) {
						if(taskRunner.OnComplete != null) taskRunner.OnComplete(taskRunner.Result);
					}
					else {
						if(taskRunner.OnError != null) taskRunner.OnError(taskRunner.Exception);
					}
				}
				catch(System.Exception exception) {
					var success = taskRunner.Exception != null ? "onError" : "onComplete";
					var errorInfo = string.Format(
						"{0} - {1}:\nSTACK:\n{2}\nEND STACK",
						exception,
						exception.Message,
						exception.StackTrace
					);
					
					if(taskRunner.Name != null) {
						var message = string.Format(
							"Thread '{0}' failed to process {1} properly. {2}",
							taskRunner.Name,
							success,
							errorInfo
						);
						Debug.LogError(message);
					}
					else {
						var message = string.Format(
							"Thread <unnamed> failed to process {0} properly. {2}",
							success,
							errorInfo
						);
						Debug.LogError(message);
					}
				}

				handlersToRemove[i] = true;
			}
		}
		finally {
			taskLock.ExitReadLock();
		}

		taskLock.EnterWriteLock();
		try {
			// reverse order so we don't munge up the list indexes while we are cleaning up
			for(var i = handlersToRemove.Length - 1; i > -1; --i) {
				if(handlersToRemove[i]) {
					taskRunners.RemoveAt(i);
				}
			}
		}
		finally {
			taskLock.ExitWriteLock();
		}


		groupLock.EnterReadLock();
		var groupList = groups.Values.ToArray();
		var groupsToRemove = new bool[groupList.Length];

		try {
			for(var i = 0; i < groupList.Length; ++i) {
				var group = groupList[i];
				if(!group.AllThreadsComplete) continue;
				
				try {
					if(!group.Async) group.OnComplete(group.Results);
				}
				catch(System.Exception exception) {
					var error = string.Format(
						"{0} - {1}\nSTACK:\n{2}\nENDSTACK\n",
						exception,
						exception.Message,
						exception.StackTrace
					);
					Debug.LogError(string.Format("Failure to handle onComplete of '{0}'. {1}", group.Name, error));
				}
				groupsToRemove[i] = true;
			}
		}
		finally {
			groupLock.ExitReadLock();
		}

		groupLock.EnterWriteLock();
		try {
			// reverse order so we don't munge up the list indexes while we are cleaning up
			for(var i = groupsToRemove.Length - 1; i > -1; --i) {
				if(groupsToRemove[i]) {
					groups.Remove(groupList[i].Name);
				}
			}
		}
		finally {
			groupLock.ExitWriteLock();
		}
	}
}
