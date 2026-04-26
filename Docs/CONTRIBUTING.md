# Contributor's Guide

Here are a couple of things to make your experience in the community more enjoyable. 

### Before you start coding, please submit the Feature Design

The code of Yafc is complicated enough that we want to make it simpler. In other words, if your feature increases the complexity, then please make a Feature Design issue with your proposal before coding, so we can discuss if this feature can be added to Yafc.

### Setting up environment
* Please inspect and run [set-up-git-hooks.sh](/set-up-git-hooks.sh) once. It sets up a formatting check to be run before `git push`, to make sure that the basic formatting is fine.

### Coding
* For the conventions that we use, please refer to the [Code Style](/Docs/CodeStyle.md).
* In Visual Studio, you can check some of the rules by running Code Cleanup, or Format Document with "Ctrl+K, Ctrl+D".

### Pull Request
* Please provide a short description of your change in the [changelog](https://github.com/Yafc-CE/yafc-ce/blob/master/changelog.txt).
* In the PR, please provide a short description of the issue, and how your PR solves that issue.
* If you need to update the branch to resolve conflicts with the master branch, then please do it by rebasing your branch, not by a merge commit.
* If you make a fix to the issue during the PR review, then please push a separate commit that fixes the issue, without rebasing the branch at the same time. That helps with the review as we need to review only the fix instead of the whole branch again. After you see that the fix commit is visible on the site, you are free to squash the fix - just make sure that Github keeps your fix commit in the history of actions. If it disappeared or is [not obvious](https://github.com/Yafc-CE/yafc-ce/pull/529#issuecomment-4013056324), then make a comment that mentions the fix commit.
