using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Languages;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Extras.Metadata.Consumers.XbmcTmdb
{
    public class XbmcTmdbSettingsValidator : AbstractValidator<XbmcTmdbMetadataSettings>
    {
        public XbmcTmdbSettingsValidator()
        {
            RuleFor(c => c.ApiKey).NotEmpty();
        }
    }

    public class XbmcTmdbMetadataSettings : IProviderConfig
    {
        private static readonly XbmcTmdbSettingsValidator Validator = new XbmcTmdbSettingsValidator();

        public XbmcTmdbMetadataSettings()
        {
            ApiKey = string.Empty;
            MovieMetadata = true;
            MovieMetadataURL = false;
            MovieMetadataLanguage = (int)Language.English;
            MovieImages = true;
            UseMovieNfo = false;
        }

        [FieldDefinition(0, Label = "API Key (Required)", Type = FieldType.Textbox, HelpLink = "https://www.themoviedb.org/documentation/api", Privacy = PrivacyLevel.ApiKey)]
        public string ApiKey { get; set; }

        [FieldDefinition(1, Label = "Movie Metadata", Type = FieldType.Checkbox)]
        public bool MovieMetadata { get; set; }

        [FieldDefinition(2, Label = "Movie Metadata URL", Type = FieldType.Checkbox, HelpText = "Radarr will write the tmdb/imdb url in the .nfo file", Advanced = true)]
        public bool MovieMetadataURL { get; set; }

        [FieldDefinition(3, Label = "Metadata Language", Type = FieldType.Select, SelectOptions = typeof(RealLanguageFieldConverter), HelpText = "Radarr will write metadata in the selected language if available")]
        public int MovieMetadataLanguage { get; set; }

        [FieldDefinition(4, Label = "Movie Images", Type = FieldType.Checkbox)]
        public bool MovieImages { get; set; }

        [FieldDefinition(5, Label = "Use Movie.nfo", Type = FieldType.Checkbox, HelpText = "Radarr will write metadata to movie.nfo instead of the default <movie-filename>.nfo")]
        public bool UseMovieNfo { get; set; }

        [FieldDefinition(6, Label = "Use <image>.jpg", Type = FieldType.Checkbox, HelpText = "Radarr will write images to <image>.jpg instead of the default <movie-filename>-<image>.jpg")]
        public bool UseMovieImages { get; set; }

        public bool IsValid => true;

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
