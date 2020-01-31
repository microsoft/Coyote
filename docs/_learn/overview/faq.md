---
layout: reference
title: Frequently asked questions
section: learn
permalink: /learn/overview/faq
---

## Frequently asked questions

<br/>


#### Seems magical, how does Coyote really work?

I'm glad you asked. See [how does it work](../overview/how.md).
<br/><br/>

#### Will using Coyote affect the total budget for a software development project? 

It will have minimal impact initially, as developers ramp up on Coyote concepts. 
In the long run, our experience with Azure teams suggests that it will help reduce
the total budget. Coyote is designed to add agility so that the development and
testing processes move at a faster pace.
<br/><br/>

#### What type of projects will benefit most from using Coyote? 

Projects that have to deal with concurrency/asynchrony or other non-deterministic
issues (such as timers, failures and re-orderings). 
<br/><br/>

#### Will Coyote slow down my production code?

No, Coyote is very lightweight.
<br/><br/>

#### How much time will developers need to learn Coyote? 

We have been working hard towards making Coyote accessible to all developers, however,
we cannot give a conclusive answer yet. We think that it won't take long.
A junior engineer that recently joined an Azure team using Coyote said: "_being 
a new developer to the team, one of the first few things I worked on was Coyote, 
it was really quick to onboard, writing actual code is simple and straightforward_".
Tell us about your experience!
<br/><br/>

#### At what stage of the development lifecycle is it best to start using Coyote? 

Ideally at the very beginning, since you need to design and build using Coyote APIs. 
It is better to do testing while developing, not after you are ready to ship :)
<br/><br/>

#### How much additional effort is needed to test an existing system with Coyote? 

Depends on what programming model and concurrency APIs you used for developing your
existing system because, for Coyote testing to work, it needs to understand the
[non-determinism](../core/non-determinism) in your system.
In some cases, this may be as simple as replacing the `Task` type with
[`ControlledTask`](../programming-models/async/overview). In other cases, you
would also have to model additional non-determinism, such as timers and external services.
In general, we recommend using Coyote in early stages of development.
<br/><br/>

#### How is Coyote being maintained and what level of support can we expect? 

Coyote is currently an Open Source project on GitHub under the MIT license.
Microsoft continues to invest in Coyote and accepts community contributions as well.
Issues can be posted there and will be resolved in a timely manner. The Coyote team
does not provide official SLAs. Having said that, the support is good enough to keep
many Azure teams happy.
<br/><br/>

#### Is Coyote only available with Azure? 

No, Coyote can run in any Windows or Linux machine, regardless if it is a local machine 
or a VM on the cloud.
<br/><br/>

#### How many machines do we need to run a Coyote program? 

A Coyote program is hosted in a .NET process, so it can easily run on just one machine. 
You can also create a distributed Coyote program by connecting multiple processes, 
each running Coyote. You are free to use any distributed host platform 
as well as any messaging system that you want.
<br/><br/>

#### Is it meaningful to use Coyote for a program that runs on a single machine only?

Sure, in fact most testing with Coyote is performed on a single machine. Many Coyote 
users today build reliable cloud services, and Coyote itself is not service-specific.
You can use it to build and test any kind of asynchronous .NET program that needs 
to be reliable.
<br/><br/>

#### How is Coyote different from other similar systems out there?

One comparison point for Coyote would be other offerings that help build reliable systems.
It would be difficult to give an exhaustive answer here, but Coyote is quite unique in its
combination of powerful testing and very light run-time requirements (i.e., it requires
the .NET framework only). Other systems might require a buy-in to a particular distributed
runtime or messaging system, or might not help much in the way of testing custom business
logic. You can adapt Coyote to work with whatever platform you are using today.
<br/><br/>
