using HD2ModCore.Domain;

namespace HD2ModCore.Application;

public interface IPatchFileNameParser
{
	bool TryParse(string fileName, out PatchFileNameInfo? info);

	PatchFileNameInfo Parse(string fileName);
}
