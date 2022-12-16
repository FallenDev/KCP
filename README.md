# KCP C# .NET 7 
Works out of the box for client & server communication. This implementation is using .NET 7, support for older frameworks was removed.

## Features:

- Asynchronous API standard interface IKcpIO.cs
  - ValueTask Recv(IBufferWriter<byte> writer, object option = null);
  - ValueTask Output(IBufferWriter<byte> writer, object option = null);
  - Comes with a basic implementation.  KcpIO.cs
- The generalization of kcpSegment can be implemented by user-defined high-performance.
  - `KcpCore<Segment>`  where Segment : IKcpSegment
  - `KcpIO<Segment>` : `KcpCore<Segment>`, IKcpIO  where Segment : IKcpSegment
  - `Kcp<Segment>` : `KcpCore<Segment>` where Segment:IKcpSegment

## Links:

c: skywind3000 [KCP](https://github.com/skywind3000/kcp)  
go: xtaci [kcp-go](https://github.com/xtaci/kcp-go)  

## Architecture:

- Unsafe code and unmanaged memory are used internally, which will not cause pressure on gc.
- Support user-defined memory management methods, if you don't want to use unsafe mode, you can use memory pool.
- For output callback and TryRecv function. Use the RentBuffer callback to allocate memory externally. Please refer to [IMemoryOwner](https://docs.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines) usage.
- Support `Span<byte>`

## Thread Safety:
Simply put: 
When thread 1 calls Recv/Update, thread 2 is also calling Recv/Update. A large number of shared data structures are used inside the function, and if locked, performance will be seriously affected.    
When thread 1 calls Send/Input, thread 2 is also calling Send/Input. There are locks inside the function.

- Send and Input can be called simultaneously from any number of threads. 
  It is safe for multiple threads to send messages at the same time, and you can safely send messages in asynchronous functions.  
- But `cannot` multiple threads call Recv and Update at the same time.
  The method with the same name can only be called by one thread at the same time, otherwise it will cause a multi-thread error. 

## Test:
Traverse to your compiled Test Client project & Test Server project. Run both, and follow the prompts on Test Client Console.

![image](https://user-images.githubusercontent.com/12104989/208140809-ed5cedc2-f878-4e98-9311-66aee2d5b413.png)

[[Fixed]~~ Two Kcp echo tests in the same process, use at least 3 threads, otherwise it may deadlock.  ~~](Image/deadlock.jpg)

Execute dotnet test under the path of UnitTestProject1 to perform multi-framework testing.  (need to install dotnetcoreSDK)

## Some changes from the C version：

| Differential change        | Version C           | C# version                                                 |
| ---------------- | -------------- | ----------------------------------------------------- |
| data structure       |                |                                                       |
| acklist          | array          | ConcurrentQueue                                       |
| snd_queue        | Doubly linked list       | ConcurrentQueue                                       |
| snd_buf          | Doubly linked list      | LinkedList                                            |
| rcv_buf          | Doubly linked list       | LinkedList                                            |
| rcv_queue        | Doubly linked list      | List                                                  |
| --------------   | -------------- | --------------                                        |
| Callback        |                | Added the RentBuffer callback, which can apply for memory from the outside when KCP needs it. |
| Multithreading          |                | Increased thread safety.                                      |
| Streaming Mode          |                | Stream mode has been removed due to data structure changes.                    |
| interval minimum interval | 10ms           | 0ms (CPU is allowed to run at full load under special circumstances)                   |
| --------------   | -------------- | --------------                                        |
| API change         |                |                                                       |
|                  |                | Increase endian encoding settings. Default little-endian encoding.                    |
|                  |                | Add the TryRecv function, and only peeksize once when Recv is possible.         |
|                  | ikcp_ack_push  | This function was removed (inlined)                                |
|                  | ikcp_ack_get   | This function was removed (inlined)                                |


---
---
## Sponsored Link

![Alipay](https://github.com/KumoKyaku/KumoKyaku.github.io/blob/develop/source/_posts/%E5%9B%BE%E5%BA%8A/alipay.png)



