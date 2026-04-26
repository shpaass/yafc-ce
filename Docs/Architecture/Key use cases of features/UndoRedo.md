The Undo and Redo functionality has the following key use cases where it drastically improves a common operation:  

Key use case:  
*Helping to fix a silently broken sheet*. The sheet is silently broken when there is no explicit error but the result is unreasonable due to linking or not specifying overproduction. It can be made worse by one noticing it only further down the line, when it's not obvious what broke it. Without Undo, fixing a large sheet may require rebuilding whole sections from scratch. The larger the sheet the harder it is to debug because one is not told clearly what the solver breaks on. In this scenario, having Undo would save a lot of time by pointing out where exactly the sheet broke.  

Less common, but still important:  
*Comparing two different recipes for the same product*. In a production chain, one can compare two recipes for one product by adding both to the sheet and repeating Undo/Redo on the "enable recipe" setting for the one that is used when both are enabled. The alternative to this use case is to clone the sheet and do the setups in two separate sheets, but it takes more time and makes it harder to spot the differences.
