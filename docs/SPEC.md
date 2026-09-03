# Original Specification

The owner's original brief, preserved verbatim (including its original
spelling and numbering). This is the source of truth for scope; every
other document in `docs/` describes how it was interpreted and built.

> The project's `README.md` held this text at the start of the project but
> was found empty on disk when work began, so it is reproduced here from
> the originating request.

---

Create a game of life. the game will be more realistic. each creature will have a basic machine learning like graph with wights. and new attributes will be added over time by birth
of new mutiations.
the game will be written in c# and will be written in the following steps:
1. create game infrastructure.
   1. Main Menu
      1. take from qlight
   2. use any nugget you need
   3. use can select:
      1. new game
      2. configuration
         1. back
      3. exit
         1. y/n clicking on ecs will exit in this stage.
2. create single creature.
   1. creature will be simple circle with eyes.
      1. eyes have range
      2. eyes have field of view (cone)
      3. eyes can detect visible things
      4. short cut show attribues to show cons
   2. Creature have nose
      1. can sense by radios feremons
      2. short cut show attribues raduis
   3. creature can move right left forword and backword.
      1. agility (speed of spin)
      2. speed (speed forward backword)
      3. movement consume energy.
   4. creature have mouth
      1. can consume greens.
      2. can consume othe creatures.
      3. short cut show attribues
   5. short cut show attribues
3. create board
   1. camera can zoom in and out
   2. camera can pen left and right
   3. boadrd generate greens
      1. we need to think abouts types
      2. how they will spreed.
4. create creatures on board
 project stucture
 docs - documents
 src -  projects directories by functionality
 dest - output
 summerize all in readme.
 create claude code
 update documents after each step
