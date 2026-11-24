// path::list_var gives a new list with the var's initial contents

/datum/a
	var/list/L = list(1, 2, "k" = 3)

/datum/a/sub

/datum/a/override
	L = list(5)

/datum/c
	var/list/L = /datum/a::L

/datum/b
	var/list/copied = /datum/a::L
	var/list/chained = /datum/c::L

/proc/RunTest()
	var/list/A = /datum/a::L
	ASSERT(islist(A) && A.len == 3 && A["k"] == 3)

	A += 4
	var/list/A2 = /datum/a::L
	ASSERT(A2.len == 3)

	var/list/sub = /datum/a/sub::L
	ASSERT(sub.len == 3)
	var/list/override = /datum/a/override::L
	ASSERT(override.len == 1 && override[1] == 5)

	var/datum/b/B = new()
	ASSERT(B.copied.len == 3)
	ASSERT(B.chained.len == 3)
	ASSERT(/datum/a::L.Copy().len == 3)
