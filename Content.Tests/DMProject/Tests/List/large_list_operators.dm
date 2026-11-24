// Operators between large lists hash their values; they must act the same as they do on small lists

/datum/thing

/proc/make_things(count)
	. = list()
	for(var/i in 1 to count)
		. += new /datum/thing

/proc/test_combine()
	var/list/things = make_things(200)
	var/list/first = things.Copy(1, 151)
	var/list/second = things.Copy(101)

	first |= second
	ASSERT(first.len == 200)
	for(var/i in 1 to 200)
		ASSERT(first[i] == things[i])

	// Duplicates on the right side are only added once
	var/list/doubled = things + things
	var/list/combined = list()
	combined |= doubled
	ASSERT(combined.len == 200)

	// Duplicates on the left side are kept
	doubled |= things
	ASSERT(doubled.len == 400)

	// Associations come with newly added values, existing ones are unchanged
	var/list/assoc_left = list("k1" = "left")
	var/list/assoc_right = list()
	for(var/i in 1 to 600)
		assoc_right["k[i]"] = i
	assoc_left |= assoc_right
	ASSERT(assoc_left.len == 600)
	ASSERT(assoc_left["k1"] == "left")
	ASSERT(assoc_left["k600"] == 600)

	// Deleted objects are null
	var/list/with_deleted = make_things(600)
	var/datum/thing/deleted = with_deleted[1]
	del(deleted)
	var/list/nulls = list(null)
	nulls |= with_deleted
	ASSERT(nulls.len == 600)

/proc/test_remove()
	var/list/things = make_things(200)
	var/list/doubled = things + things
	var/list/removed = things.Copy(51, 151)

	// The last occurrence is what gets removed, and order is kept
	doubled -= removed
	ASSERT(doubled.len == 300)
	for(var/i in 1 to 200)
		ASSERT(doubled[i] == things[i])
	for(var/i in 1 to 50)
		ASSERT(doubled[200 + i] == things[i])
		ASSERT(doubled[250 + i] == things[150 + i])

	// Each occurrence on the right side removes one
	var/list/copy = things + things
	copy -= things + things
	ASSERT(copy.len == 0)

	var/list/difference = things - removed
	ASSERT(difference.len == 100)
	ASSERT(things.len == 200)

	var/list/assoc = list()
	for(var/i in 1 to 600)
		assoc["k[i]"] = i
	var/list/keys = list()
	for(var/i in 1 to 300)
		keys += "k[i]"
	assoc -= keys
	ASSERT(assoc.len == 300)
	ASSERT(isnull(assoc["k1"]))
	ASSERT(assoc["k301"] == 301)
	assoc["k1"] = "new"
	ASSERT(assoc.len == 301)
	ASSERT(assoc[301] == "k1")

/proc/test_assoc_set()
	// Associating a value that's already in the list doesn't add it again
	var/list/L = list("a", "b")
	L["a"] = 1
	ASSERT(L.len == 2)
	L["b"] = 2
	ASSERT(L.len == 2)
	L["c"] = 3
	ASSERT(L.len == 3)
	L["a"] = 4
	ASSERT(L.len == 3 && L["a"] == 4)

	// Duplicates only have the one association
	L = list("a", "a")
	L["a"] = 1
	ASSERT(L.len == 2)
	L["b"] = 2
	ASSERT(L.len == 3)
	L["a"] = 3
	ASSERT(L.len == 3)

	// Replacing an associated value
	L = list("a" = 1)
	L[1] = "b"
	L["b"] = 2
	ASSERT(L.len == 1 && L["b"] == 2)
	L["c"] = 3
	ASSERT(L.len == 2 && L[2] == "c")

	// Adding a value without an association, then associating it
	L = list("a" = 1)
	L += "b"
	L["b"] = 2
	ASSERT(L.len == 2)

	// Same for after a Cut() or Swap()
	L = list("a" = 1, "b" = 2, "c" = 3)
	L.Swap(1, 3)
	L["a"] = 5
	L["c"] = 6
	ASSERT(L.len == 3 && L[1] == "c" && L[3] == "a")
	L.Cut(2, 3)
	L["b"] = 7
	ASSERT(L.len == 3 && L[3] == "b")

/proc/RunTest()
	test_combine()
	test_remove()
	test_assoc_set()
