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
	static Dictionary<string, ThreadGroup> groups = new Dictionary<string, ThreadGroup>();
	static List<ThreadHandler> threadHandlers = new List<ThreadHandler>();
	
	class ThreadHandler {
		public string Name {get; set;}
		public string GroupName { get; set; }
		public int PositionInGroup { get; set; }
		public System.Func<object> Job;
		public bool IsComplete { get; set; }
		public System.Action<object> OnComplete { get; set; }
		public System.Action<System.Exception> OnError { get; set; }
		public System.Action<object[], int> OnGroupComplete { get; set; }
		public object Result { get; set; }
		public object[] GroupResults { get; set; }
		public System.Exception Exception { get; set; }
	}
	
	class ThreadGroup {
		private int ThreadsComplete { get; set; }
		public string Name { get; set; }
		// TODO: not sure how else to handle this. This creates a problem with dynamic groups though - perhaps should be
		// mutated as the number of threads changes?
		public int TotalThreads { get; set; }
		public object[] Results { get; set; }
		public List<ThreadHandler> ThreadHandlers { get; set; }
		public System.Action<object[]> OnComplete { get; set; }
		public bool AllThreadsComplete { get { return ThreadsComplete == TotalThreads; } }
		
		public ThreadGroup() {
			ThreadHandlers = new List<ThreadHandler>();
		}
		
		public void Complete(ThreadHandler threadHandler) {
			++ThreadsComplete;
		}
	}
	
	// TODO: Add a mechanism for grouping threads (a groupOnComplete and groupOnError). Great for batched jobs
	// TODO: See about making this generic somehow
	/// <summary>
	/// Adds the delegate to the ThreadPool's worker queue as a thread.
	/// Threads constructed in this manner are cleaned up automatically on application exit.
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
	public static void AddThread(
		string name,
		System.Func<object> jobToThread,
		System.Action<object> onComplete,
		System.Action<System.Exception> onError) {
		
		EnsureUniThreadRunner();
		
		var threadHandler = new ThreadHandler {
			Name = name,
			OnComplete = onComplete,
			OnError = onError,
			Job = jobToThread,
		};
		ThreadPool.QueueUserWorkItem(new WaitCallback(ThreadExecuter), threadHandler);
		Add(threadHandler);
	}
	
	public static void AddThread(
		string name,
		string groupName,
		System.Func<object> jobToThread,
		System.Action<object> onComplete,
		System.Action<System.Exception> onError,
		System.Action<object[], int> onGroupComplete) {
		
		EnsureUniThreadRunner();
		
//		if(!groups.ContainsKey(groupName)) {
//			groups[groupName] = new ThreadGroup { Name = groupName,};
//		}
		if(!groups.ContainsKey(groupName)) {
			throw new System.Exception(string.Format(
				"No group named '{0}' found. Declare with UniThread.AddGroupHandler",
				groupName
			));
		}
		lock(groups) {
			var group = groups[groupName];
			var position = group.ThreadHandlers.Count;
			var threadHandler = new ThreadHandler {
				Name = name,
				GroupName = groupName,
				OnComplete = onComplete,
				OnError = onError,
				OnGroupComplete = onGroupComplete,
				Job = jobToThread,
				PositionInGroup = position,
			};
			Add(threadHandler);
			group.ThreadHandlers.Add(threadHandler);
			ThreadPool.QueueUserWorkItem(new WaitCallback(ThreadExecuter), threadHandler);
		}
	}
	
	public static void CreateGroup(string groupName, int totalThreads, System.Action<object[]> onComplete) {
		// TODO: May need to terminate old threads on the old group if this is squashing it
		lock(groups) {
			groups[groupName] = new ThreadGroup{Name = groupName, TotalThreads = totalThreads, OnComplete = onComplete};
		}
	}
	
	static void ThreadExecuter(System.Object state) {
		try {
			var threadHandler = state as ThreadHandler;
			try {
				var result = threadHandler.Job();
				threadHandler.Result = result;
			}
			catch(System.Exception exception) {
				threadHandler.Exception = exception;
			}
			threadHandler.IsComplete = true;
			lock(groups) {
				if(threadHandler.GroupName != null) {
					try {
						var group = groups[threadHandler.GroupName];
						group.Complete(threadHandler);
						if(group.AllThreadsComplete) {
							group.Results = group.ThreadHandlers.Select(t => t.Result).ToArray();
						}
					}
					catch(KeyNotFoundException) {
						Debug.LogError(string.Format("Could not find group '{0}'", threadHandler.GroupName));
					}
				}
			}
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
	
	static void EnsureUniThreadRunner() {
		if(UniThread.threadRunner != null) return;
	
		var threadRunnerGameObject = GameObject.Find(UNI_THREAD_RUNNER_GAME_OBJECT_NAME);
		if(threadRunnerGameObject == null) {
			threadRunnerGameObject = new GameObject(UNI_THREAD_RUNNER_GAME_OBJECT_NAME, typeof(UniThread));
		}
		var threadRunner = threadRunnerGameObject.GetComponent<UniThread>();
		if(threadRunner == null) threadRunner = threadRunnerGameObject.AddComponent<UniThread>();
	}
	
	static void Add(ThreadHandler threadHander) {
		lock(threadHandlers) {
			threadHandlers.Add(threadHander);
		}
	}
	
	void Awake() {
		DontDestroyOnLoad(gameObject);
	}
	
	void Update() {
		lock(threadHandlers) {
			var handlersToRemove = new bool[threadHandlers.Count];
			for(var i = 0; i < threadHandlers.Count; ++i) {
				var threadHandler = threadHandlers[i];
				if(!threadHandler.IsComplete) continue;
				
				try {
					if(threadHandler.Exception == null) {
						if(threadHandler.OnComplete != null) threadHandler.OnComplete(threadHandler.Result);
						if(threadHandler.GroupName != null && threadHandler.OnGroupComplete != null) {
							threadHandler.OnGroupComplete(threadHandler.GroupResults, threadHandler.PositionInGroup);
						}
					}
					else {
						if(threadHandler.OnError != null) threadHandler.OnError(threadHandler.Exception);
					}
				}
				catch(System.Exception exception) {
					var success = threadHandler.Exception != null ? "onError" : "onComplete";
					var errorInfo = string.Format(
						"{0} - {1}:\nSTACK:\n{2}\nEND STACK",
						exception,
						exception.Message,
						exception.StackTrace
					);
					
					if(threadHandler.Name != null) {
						var message = string.Format(
							"Thread '{0}' failed to process {1} properly. {2}",
							threadHandler.Name,
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
			
			// reverse order so we don't munge up the list indexes while we are cleaning up
			for(var i = handlersToRemove.Length - 1; i > -1; --i) {
				if(handlersToRemove[i]) {
					threadHandlers.RemoveAt(i);
				}
			}
		}
		lock(groups) {
			var groupList = groups.Values.ToArray();
			var groupsToRemove = new bool[groupList.Length];
			for(var i = 0; i < groupList.Length; ++i) {
				var group = groupList[i];
				if(!group.AllThreadsComplete) continue;
				
				try {
					if(group.OnComplete != null) group.OnComplete(group.Results);
				}
				catch(System.Exception exception) {
					var error = string.Format(
						"{0} - {1}\nSTACK:\n{2}\nENDSTACK\n",
						exception,
						exception.Message,
						exception.StackTrace
					);
					Debug.LogError(string.Format("Failture to handle onComplete of '{0}'. {1}", group.Name, error));
				}
				groupsToRemove[i] = true;
			}
			
			// reverse order so we don't munge up the list indexes while we are cleaning up
			for(var i = groupsToRemove.Length - 1; i > -1; --i) {
				if(groupsToRemove[i]) groups.Remove(groupList[i].Name);
			}
		}
	}
}
