---
layout: reference
section: learn
title: Demo
permalink: /learn/programming-models/actors/state-machine-demo
---

<div>

{% include Raft.svg %}

<script language="javascript" src="/coyote/assets/js/animate_trace.js"></script>
<script language="javascript" src="/coyote/assets/js/trace_model.js"></script>

<script language="javascript">

fetchTrace('/coyote/assets/data/Raft.xml', convertTrace);

</script>
</div>

This graph was generated from a `coyote test` trace of a `StateMachine` based coyote application that implements the `Raft` protocol.
The fact that coyote `StateMachines` expose explicit state information makes it possible for `coyote` to visualize what is going on
in a level of detail that is hard to extract from other kinds of C# code.  This illustrates the benefit of this programming model
both for software design, as well as implementation enforcing this design, and testing that can find very hard to find bugs in these
kinds complex distributed systems.

The trace shows a global order of messages being transferred between the various state machines. The message exchange is shown to happen
one after the other (when in reality they may be happening in parallel). This makes it easy to understand the trace. This trace, in fact, 
demonstrates a bug where two Raft `Server` state-machines both end up claming to be leaders: this is a violation of Raft's 
consensus requirements that there be at most one leader at a time.
