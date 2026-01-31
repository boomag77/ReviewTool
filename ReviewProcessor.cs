namespace ReviewTool
{
    internal sealed class ReviewProcessor
    {

        public enum ReviewMode
        {
            Initial,
            Final
        }

        public ReviewMode Mode
        {
            get => field;
            init;
        }

        public MainWindowViewModel ViewModel
        {
            get => field;
            init;
        }

        public ReviewProcessor(ReviewMode mode, MainWindowViewModel viewModel)
        {
            Mode = mode;
            ViewModel = viewModel;
        }

        public void StartReview()
        {
            if (Mode == ReviewMode.Initial)
            {
                // Initial review logic
                ViewModel.IsInitialReview = true;
            }
            else if (Mode == ReviewMode.Final)
            {
                // Final review logic
                ViewModel.IsInitialReview = false;
            }
        }
    }
}
