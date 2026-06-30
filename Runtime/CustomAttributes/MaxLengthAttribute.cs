namespace ArtificeToolkit.Attributes
{
    public class MaxLengthAttribute : ValidatorAttribute
    {
        public int Length;
        
        public MaxLengthAttribute(int length)
        {
            Length = length;
        }
    }
}
