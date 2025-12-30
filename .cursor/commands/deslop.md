Clean up AI slop and boilerplate in this codebase:
Remove unnecessary comments:
Delete obvious/redundant comments (e.g., // Initialize the variable, // Return the result)
Remove commented-out code blocks
Keep only comments that explain why, not what
Eliminate verbose boilerplate:
Convert verbose null checks to null-conditional operators (?., ??)
Use expression-bodied members where methods are simple one-liners
Replace explicit type declarations with var where type is obvious
Collapse single-use variables that add no clarity
Remove AI-isms:
Delete "helper" methods that are only called once and add no abstraction value
Remove excessive validation/error handling for impossible states
Eliminate unnecessary interfaces for classes with only one implementation
Remove redundant ToString() calls in string interpolation
Delete empty catch blocks or overly defensive try-catches
Simplify patterns:
Replace if (x == true) with if (x)
Replace if (x == false) with if (!x)
Use is null / is not null instead of == null / != null
Convert if-else that just returns booleans to single return statement
Use LINQ where it improves readability
Keep the code functional - don't break anything, just make it cleaner.
Scan the entire codebase and make these changes file by file.
