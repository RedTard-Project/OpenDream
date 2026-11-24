// Static vars in procs that are initialized by a proc call make those calls in declaration order,
// even if an earlier one depends on something declared later

/datum/holder/proc/first()
	var/static/x = world.call_late()
	return x

/proc/second()
	var/static/x = add_call("second")
	return x

/world/proc/call_late()
	return add_call("late")

var/list/calls

/proc/add_call(name)
	calls = (calls || list()) + name
	return 1

/proc/RunTest()
	ASSERT(calls.Find("late") < calls.Find("second"))
