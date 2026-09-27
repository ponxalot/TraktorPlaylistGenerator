var generator = new TraktorPlaylistGenerator(
    musicRoot: @"V:\Audio\Musik\DJ",
    collectionPath: @"V:\Audio\Musik\DJ\_TraktorData\collection.nml"
);

generator.Generate();
